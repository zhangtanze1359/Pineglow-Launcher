import pathlib,re
here=pathlib.Path(__file__).parent
root=pathlib.Path((here/'credential-proof-root.txt').read_text(encoding='utf-8-sig').strip())
p=root/'dev/PineglowLauncher/Pineglow.Launcher.Services/SecretStore.cs'
s=p.read_text(encoding='utf-8-sig')
read='\n\t\tFile.AppendAllText(Environment.GetEnvironmentVariable("PINEGLOW_ACCEPTANCE_SECRET_EVENTS")!, JsonSerializer.Serialize(new { name, operation = "read-stub" }) + Environment.NewLine);\n\t\treturn string.Empty;'
write='\n\t\tFile.AppendAllText(Environment.GetEnvironmentVariable("PINEGLOW_ACCEPTANCE_SECRET_EVENTS")!, JsonSerializer.Serialize(new { name, operation = string.IsNullOrEmpty(value) ? "delete-intent" : "write-intent" }) + Environment.NewLine);\n\t\treturn;'
s,n=re.subn(r'(public static string Load\(string name\)\s*\{\s*ValidateName\(name\);)',lambda m:m[1]+read,s);assert n==1
s,n=re.subn(r'(public static void Save\(string name, string\? value\)\s*\{\s*ValidateName\(name\);)',lambda m:m[1]+write,s);assert n==1
p.write_text(s,encoding='utf-8');(here/'SecretStore.instrumented-copy.cs').write_text(s,encoding='utf-8')
print('instrumented isolated copy; native credential reads/writes bypassed')
