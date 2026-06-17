import os
import subprocess
import tempfile
import textwrap
import platform

AURSH = os.path.join('bin', 'aursh.exe' if platform.system() == 'Windows' else 'aursh')
if not os.path.exists(AURSH):
    AURSH = 'aursh'

cases = [
    ('functions_args_return', r'''
foo() {
  echo "args:$#:$1:$2"
  return 7
}
foo one two
echo "ret:$?"
''', 'args:2:one:two\nret:7\n', 0),
    ('case_glob', r'''
word=abc
case "$word" in
  a*) echo match ;;
  *) echo miss ;;
esac
''', 'match\n', 0),
    ('compound_redirect', r'''
rm -f aursh_audit_tmp.txt
if true; then
  echo redirected
fi > aursh_audit_tmp.txt
echo "file:$(cat aursh_audit_tmp.txt)"
rm -f aursh_audit_tmp.txt
''', 'file:redirected\n', 0),
    ('block_redirect', r'''
  rm -f aursh_audit_tmp.txt
  { echo one; echo two; } > aursh_audit_tmp.txt
  echo "file:$(cat aursh_audit_tmp.txt)"
  rm -f aursh_audit_tmp.txt
  ''', 'file:one\ntwo\n', 0),
    ('temp_env_assignment', r'''
  FOO=global
  FOO=temp sh -c "printf %s $FOO"
  echo
  echo "after:$FOO"
  ''', 'global\nafter:global\n', 0),
    ('and_or', r'''
false && echo bad || echo good
true || echo bad
''', 'good\n', 0),
    ('nested_break', r'''
for outer in 1 2; do
  for inner in a b; do
    echo "$outer/$inner"
    break 2
  done
done
''', '1/a\n', 0),
    ('quoted_array_at', r'''
set -- "a b" c
for x in "$@"; do echo "[$x]"; done
''', '[a b]\n[c]\n', 0),
]

for name, script, expected_out, expected_code in cases:
    fd, path = tempfile.mkstemp(suffix='.aur', dir='tests', text=True)
    os.close(fd)
    with open(path, 'w', newline='\n') as f:
        f.write(textwrap.dedent(script).lstrip())
    try:
        p = subprocess.run([AURSH, path], capture_output=True, text=True, timeout=20)
        ok = (p.returncode == expected_code and p.stdout == expected_out)
        print(f'[{"PASS" if ok else "FAIL"}] {name}')
        if not ok:
            print('code', p.returncode, 'expected', expected_code)
            print('stdout:', repr(p.stdout), 'expected:', repr(expected_out))
            print('stderr:', repr(p.stderr))
    finally:
        try:
            os.remove(path)
        except OSError:
            pass
