#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Prompt Lab —— Eresoth RTS 指挥链路的本地调试服务器（零依赖，只用 Python 标准库）。

用法：
    python AI_RTS/tools/prompt_lab.py        # 启动后自动打开 http://127.0.0.1:8735

功能：
    1. 读取并解析 llm_log.jsonl / command_log.jsonl（v1/v2 字段自适应）
    2. 读写 EresothRTS/prompts/ 下的 prompt 文件（游戏内热重载，保存即生效）
    3. 代理 LLM 调用（避开浏览器 CORS，api_key 不出服务端）并回传 content/reasoning/usage
    4. 读写批跑样例 tools/test_cases.json

prompt 组装规则与游戏内 PromptBuilder 完全一致：同一模板、同一占位符、同一"（无）"历史兜底，
保证这里测的就是游戏里跑的。
"""

import json
import os
import threading
import time
import urllib.error
import urllib.request
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

HERE = os.path.dirname(os.path.abspath(__file__))          # AI_RTS/tools
ROOT = os.path.dirname(HERE)                               # AI_RTS
PROJ = os.path.join(ROOT, "EresothRTS")
PROMPT_DIR = os.path.join(PROJ, "prompts")
LLM_LOG = os.path.join(PROJ, "llm_log.jsonl")
CMD_LOG = os.path.join(PROJ, "command_log.jsonl")
CASES_FILE = os.path.join(HERE, "test_cases.json")
SAMPLE_DIGEST_FILE = os.path.join(HERE, "sample_digest.json")
CONFIG_FILE = os.path.join(PROJ, "llm_config.json")
HTML_FILE = os.path.join(HERE, "prompt_lab.html")
PORT = 8735

PROMPT_FILES = ("system_prompt.txt", "user_template.txt", "examples.txt")


# ---------------- 小工具 ----------------

def read_json_file(path, default=None):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception:
        return default


def write_json_file(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, ensure_ascii=False, indent=2)


def read_text(path):
    with open(path, "r", encoding="utf-8") as f:
        return f.read()


def tail_jsonl(path, n):
    """读 jsonl 最后 n 行并逐行解析；坏行保留原文打 _bad 标记。"""
    if not os.path.exists(path):
        return {"path": path, "lines": [], "missing": True}
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        lines = f.readlines()
    if n > 0:
        lines = lines[-n:]
    out = []
    for i, ln in enumerate(lines):
        ln = ln.strip()
        if not ln:
            continue
        try:
            out.append(json.loads(ln))
        except Exception:
            out.append({"_bad": True, "raw": ln[:500]})
    return {"path": path, "lines": out, "total": len(lines)}


def load_prompts():
    """读 prompts/ 三个文件；返回 (dict|None, 错误信息)。"""
    result = {}
    for name in PROMPT_FILES:
        p = os.path.join(PROMPT_DIR, name)
        if not os.path.exists(p):
            return None, f"缺少 prompt 文件：{p}"
        result[name] = read_text(p).strip()
    return result, None


def prompt_src_stamp():
    """与 PromptBuilder.SourceStamp() 对应的来源戳。"""
    parts = []
    for name in PROMPT_FILES:
        p = os.path.join(PROMPT_DIR, name)
        if os.path.exists(p):
            parts.append(name + "@" + time.strftime("%m-%d %H:%M", time.localtime(os.path.getmtime(p))))
        else:
            parts.append(name + "=missing")
    return " | ".join(parts)


def build_user_prompt(tpl, digest_str, history, examples, player_text):
    """与 PromptBuilder.UserPrompt 完全一致的替换规则。"""
    return (tpl.replace("{digest}", digest_str or "")
               .replace("{history}", history.strip() if history.strip() else "（无）")
               .replace("{examples}", examples)
               .replace("{player_text}", player_text or ""))


def resolve_digest(marker_or_json):
    """digest 字段：'@sample' / '@command_log_latest' / 字面 JSON 字符串。"""
    if marker_or_json == "@sample":
        if os.path.exists(SAMPLE_DIGEST_FILE):
            return read_text(SAMPLE_DIGEST_FILE).strip(), None
        return None, "sample_digest.json 不存在"
    if marker_or_json == "@command_log_latest":
        data = tail_jsonl(CMD_LOG, 500)
        for entry in reversed(data["lines"]):
            d = entry.get("digest")
            if isinstance(d, dict):
                return json.dumps(d, ensure_ascii=False), None
            if isinstance(d, str) and d.strip().startswith("{"):
                return d, None
        return None, "command_log.jsonl 里没有可用的 digest"
    s = (marker_or_json or "").strip()
    if not s:
        return None, "digest 为空"
    try:
        json.loads(s)
    except Exception as e:
        return None, f"digest 不是合法 JSON：{e}"
    return s, None


# ---------------- LLM 调用（与 LlmClient 的参数兼容逻辑一致） ----------------

def is_kimi(model, base_url):
    s = ((model or "") + (base_url or "")).lower()
    return "kimi" in s or "moonshot" in s


def is_thinking(model, force):
    m = (model or "").lower()
    return bool(force) or "thinking" in m or "reason" in m


def call_llm(cfg, system, user):
    """返回 dict：ok/content/reasoning/usage/ms/status/error/response_raw。"""
    base = (cfg.get("base_url") or "").rstrip("/")
    model = cfg.get("model") or ""
    thinking = is_thinking(model, cfg.get("thinking"))
    kimi = is_kimi(model, base)
    payload = {
        "model": model,
        "temperature": 1 if (kimi or thinking) else 0.2,
        "messages": [
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ],
    }
    if not kimi and not thinking:
        payload["response_format"] = {"type": "json_object"}
    url = base + "/chat/completions"
    req = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json",
                 "Authorization": "Bearer " + (cfg.get("api_key") or "")},
        method="POST")
    t0 = time.time()
    try:
        with urllib.request.urlopen(req, timeout=60 if thinking else 30) as resp:
            raw = resp.read().decode("utf-8", "replace")
            status = resp.status
    except urllib.error.HTTPError as e:
        ms = int((time.time() - t0) * 1000)
        raw = e.read().decode("utf-8", "replace")
        msg = raw
        try:
            j = json.loads(raw)
            msg = (j.get("error") or {}).get("message") or j.get("message") or raw
        except Exception:
            pass
        return {"ok": False, "error": f"HTTP {e.code}：{msg}", "status": e.code,
                "ms": ms, "response_raw": raw[:4000]}
    except Exception as e:
        ms = int((time.time() - t0) * 1000)
        return {"ok": False, "error": f"请求失败：{e}", "status": 0, "ms": ms}

    ms = int((time.time() - t0) * 1000)
    out = {"ok": True, "status": status, "ms": ms}
    try:
        j = json.loads(raw)
        msg = (j.get("choices") or [{}])[0].get("message") or {}
        out["content"] = msg.get("content")
        out["reasoning"] = msg.get("reasoning_content")
        out["finish_reason"] = (j.get("choices") or [{}])[0].get("finish_reason")
        out["usage"] = j.get("usage")
        if not (out["content"] or "").strip():
            out["ok"] = False
            out["error"] = "content 为空（思考模型可能因 max_tokens 截断）"
            out["response_raw"] = raw[:4000]
    except Exception as e:
        out["ok"] = False
        out["error"] = f"响应解析失败：{e}"
        out["response_raw"] = raw[:4000]
    return out


# ---------------- HTTP 服务 ----------------

class Handler(BaseHTTPRequestHandler):
    server_version = "PromptLab/1.0"

    def log_message(self, fmt, *args):  # 静音默认访问日志
        pass

    def _send_json(self, obj, status=200):
        data = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _body(self):
        n = int(self.headers.get("Content-Length") or 0)
        if n <= 0:
            return {}
        return json.loads(self.rfile.read(n).decode("utf-8"))

    def _query(self):
        from urllib.parse import urlparse, parse_qs
        q = parse_qs(urlparse(self.path).query)
        return {k: v[0] for k, v in q.items()}

    # ---------- GET ----------
    def do_GET(self):
        path = self.path.split("?")[0]
        if path in ("/", "/index.html"):
            data = read_text(HTML_FILE).encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self.end_headers()
            self.wfile.write(data)
            return
        try:
            if path == "/api/logs":
                q = self._query()
                which = q.get("which", "llm")
                n = int(q.get("n", "300"))
                self._send_json(tail_jsonl(LLM_LOG if which == "llm" else CMD_LOG, n))
            elif path == "/api/prompts":
                files = {}
                for name in PROMPT_FILES:
                    p = os.path.join(PROMPT_DIR, name)
                    if os.path.exists(p):
                        files[name] = {"exists": True, "content": read_text(p),
                                       "mtime": time.strftime("%Y-%m-%d %H:%M:%S",
                                                              time.localtime(os.path.getmtime(p)))}
                    else:
                        files[name] = {"exists": False, "content": "", "mtime": None}
                self._send_json({"dir": PROMPT_DIR, "files": files})
            elif path == "/api/cases":
                self._send_json({"cases": read_json_file(CASES_FILE, [])})
            elif path == "/api/sample_digest":
                if os.path.exists(SAMPLE_DIGEST_FILE):
                    self._send_json({"digest": read_text(SAMPLE_DIGEST_FILE).strip()})
                else:
                    self._send_json({"error": "sample_digest.json 不存在"}, 404)
            elif path == "/api/config":
                cfg = read_json_file(CONFIG_FILE, {}) or {}
                key = cfg.get("api_key") or ""
                self._send_json({
                    "base_url": cfg.get("base_url", ""), "model": cfg.get("model", ""),
                    "thinking": bool(cfg.get("thinking")), "has_key": bool(key),
                    "key_tail": ("…" + key[-4:]) if key else "",
                    "config_path": CONFIG_FILE, "config_exists": os.path.exists(CONFIG_FILE),
                })
            else:
                self._send_json({"error": "not found"}, 404)
        except Exception as e:
            self._send_json({"error": str(e)}, 500)

    # ---------- PUT ----------
    def do_PUT(self):
        path = self.path.split("?")[0]
        try:
            body = self._body()
            if path.startswith("/api/prompts/"):
                name = path.rsplit("/", 1)[-1]
                if name not in PROMPT_FILES:
                    self._send_json({"error": "非法 prompt 文件名"}, 400)
                    return
                os.makedirs(PROMPT_DIR, exist_ok=True)
                with open(os.path.join(PROMPT_DIR, name), "w", encoding="utf-8") as f:
                    f.write(body.get("content", ""))
                self._send_json({"ok": True, "mtime": time.strftime(
                    "%Y-%m-%d %H:%M:%S", time.localtime(os.path.getmtime(os.path.join(PROMPT_DIR, name))))})
            elif path == "/api/cases":
                cases = body.get("cases")
                if not isinstance(cases, list):
                    self._send_json({"error": "cases 必须是数组"}, 400)
                    return
                write_json_file(CASES_FILE, cases)
                self._send_json({"ok": True, "count": len(cases)})
            elif path == "/api/config":
                cfg = read_json_file(CONFIG_FILE, {}) or {}
                for k in ("base_url", "model"):
                    if k in body:
                        cfg[k] = body[k]
                if "thinking" in body:
                    cfg["thinking"] = bool(body["thinking"])
                if body.get("api_key"):      # 留空 = 保持现有 key
                    cfg["api_key"] = body["api_key"]
                write_json_file(CONFIG_FILE, cfg)
                self._send_json({"ok": True, "has_key": bool(cfg.get("api_key"))})
            else:
                self._send_json({"error": "not found"}, 404)
        except Exception as e:
            self._send_json({"error": str(e)}, 500)

    # ---------- POST ----------
    def do_POST(self):
        path = self.path.split("?")[0]
        if path != "/api/run":
            self._send_json({"error": "not found"}, 404)
            return
        try:
            body = self._body()
            cfg = read_json_file(CONFIG_FILE, {}) or {}
            if not cfg.get("api_key"):
                self._send_json({"ok": False, "error": f"未配置 api_key（{CONFIG_FILE}），请先在页面右上角\"设置\"里填写"})
                return
            prompts, err = load_prompts()
            if err:
                self._send_json({"ok": False, "error": err})
                return
            digest_str, err = resolve_digest(body.get("digest"))
            if err:
                self._send_json({"ok": False, "error": err})
                return
            system = prompts["system_prompt.txt"]
            user = build_user_prompt(prompts["user_template.txt"], digest_str,
                                     body.get("history") or "", prompts["examples.txt"],
                                     body.get("player_text") or "")
            result = call_llm(cfg, system, user)
            result["system"] = system
            result["user"] = user
            result["prompt_src"] = prompt_src_stamp()
            self._send_json(result)
        except Exception as e:
            self._send_json({"ok": False, "error": str(e)}, 500)


def main():
    if not os.path.exists(HTML_FILE):
        print("缺少 " + HTML_FILE)
        raise SystemExit(1)
    server = ThreadingHTTPServer(("127.0.0.1", PORT), Handler)
    url = f"http://127.0.0.1:{PORT}"
    print(f"Prompt Lab 已启动：{url}")
    print(f"日志目录：{PROJ}")
    print("按 Ctrl+C 停止")
    threading.Timer(0.5, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n已停止")


if __name__ == "__main__":
    main()
