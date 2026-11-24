import io

p = 'dev/PineglowLauncher.Tests/SecretHygieneTests.cs'
s = io.open(p, encoding='utf-8').read()

# C# 字符字面量里的反斜杠必须写成两个反斜杠：'\\'
BAD1 = "Replace('" + "\\" + "', '/')"
GOOD1 = "Replace('" + "\\\\" + "', '/')"
BAD2 = "TrimStart('" + "\\" + "', '/')"
GOOD2 = "TrimStart('" + "\\\\" + "', '/')"

fixed = 0
if BAD1 in s:
    s = s.replace(BAD1, GOOD1)
    fixed += 1
if BAD2 in s:
    s = s.replace(BAD2, GOOD2)
    fixed += 1

io.open(p, 'w', encoding='utf-8', newline='\n').write(s)
print('replaced:', fixed)
