# 校验 PromptBuilder.cs 内嵌默认与 prompts/*.txt 完全一致（fallback 一致性）
import re, os, sys

here = os.path.dirname(os.path.abspath(__file__))
cs = open(os.path.join(here, "..", "EresothRTS", "Assets", "Scripts", "Command",
                       "PromptBuilder.cs"), encoding="utf-8").read()

pairs = [("DefaultSystem", "system_prompt.txt"),
         ("DefaultExamples", "examples.txt"),
         ("DefaultUserTemplate", "user_template.txt")]

ok = True
for const, fname in pairs:
    m = re.search(r'const string %s = @"(.*?)";' % const, cs, re.S)
    assert m, const + " not found"
    embedded = m.group(1).replace('""', '"').strip()
    filec = open(os.path.join(here, "..", "EresothRTS", "prompts", fname),
                 encoding="utf-8").read().strip()
    same = embedded == filec
    ok &= same
    print(f"{fname}: {'MATCH' if same else 'DIFFER'} (embedded {len(embedded)} chars, file {len(filec)} chars)")
sys.exit(0 if ok else 1)
