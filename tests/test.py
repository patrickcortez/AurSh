import subprocess
import sys
import time
import os
import platform
import tempfile

def get_aursh_cmd():
    ext = ".exe" if platform.system() == "Windows" else ""
    local_bin = os.path.join("bin", f"aursh{ext}")
    if os.path.exists(local_bin):
        return [local_bin]
    
    dll_path = os.path.join("bin", "aursh.dll")
    if os.path.exists(dll_path):
        return ["dotnet", dll_path]
        
    return ["aursh"]

AURSH_CMD = get_aursh_cmd()

def test(args=None):
    if not args:
        print("testing AurSh...")
        time.sleep(4)
        Executed = subprocess.run(AURSH_CMD + ["-c", "echo ok"], capture_output=True, text=True)
    else:
        print("testing AurSh...")
        time.sleep(4)
        Executed = subprocess.run(AURSH_CMD + ["-c"] + args, capture_output=True, text=True)
        
    if Executed.returncode != 0:
        print("ERROR: AurSh failed to run.")
        print("--- STDOUT ---")
        print(Executed.stdout)
        print("--- STDERR ---")
        print(Executed.stderr)
        
    return Executed.returncode

def test_ssh_registration():
    print("testing aursh-ssh registration...")
    time.sleep(1)
    Executed = subprocess.run(AURSH_CMD + ["-c", "type aursh-ssh"], capture_output=True, text=True)
    if Executed.returncode != 0:
        print("ERROR: aursh-ssh test failed.")
        print("--- STDOUT ---")
        print(Executed.stdout)
        print("--- STDERR ---")
        print(Executed.stderr)
    return Executed.returncode

def test_interpreter_regressions():
    print("testing interpreter regressions...")
    expected = "\n".join([
        "Processing guest...",
        "Found admin! Breaking early.",
        "While loop count: 0",
        "While loop count: 2",
        "Nested 1/a",
        "Nested 2/a",
        "Grep result: apple banana cherry",
        "",
    ])
    
    script_path = os.path.join('tests', 'interpreter_regression.aur')
    with open(script_path, 'r') as f:
        content = f.read()
        
    # Strip existing shebang if one was committed to the repository
    if content.startswith('#!'):
        content = content.split('\n', 1)[1] if '\n' in content else ''

    expected_path = os.path.abspath(AURSH_CMD[0] if platform.system() == 'Windows' else AURSH_CMD[0])
    if expected_path.endswith('dotnet') and len(AURSH_CMD) > 1:
        expected_path = os.path.abspath(AURSH_CMD[1])
    if platform.system() == 'Windows' and not expected_path.endswith('.exe') and not expected_path.endswith('.dll'):
        expected_path += '.exe'
        
    # Write the script to a temporary file with the correct OS-specific shebang
    with tempfile.NamedTemporaryFile(mode='w', delete=False, suffix='.aur') as temp_f:
        temp_f.write('#!' + expected_path + '\n' + content)
        temp_script_path = temp_f.name

    try:
        executed = subprocess.run(AURSH_CMD + [temp_script_path], capture_output=True, text=True)
    finally:
        if os.path.exists(temp_script_path):
            os.remove(temp_script_path)

    if executed.returncode != 0 or executed.stdout != expected:
        print("ERROR: interpreter regression test failed.")
        print("--- EXPECTED STDOUT ---")
        print(expected)
        print("--- STDOUT ---")
        print(executed.stdout)
        print("--- STDERR ---")
        print(executed.stderr)
        return 1

    incomplete = subprocess.run(
        AURSH_CMD + ["-c", "while true; do echo nope"],
        capture_output=True,
        text=True,
    )
    if incomplete.returncode == 0:
        print("ERROR: incomplete construct unexpectedly succeeded.")
        print("--- STDOUT ---")
        print(incomplete.stdout)
        print("--- STDERR ---")
        print(incomplete.stderr)
        return 1

    return 0

if(__name__=="__main__"):
    args = sys.argv[1:]

    if len(args) == 0: 
        res = test()
        if res == 0:
            print("AurSh is functional")
        else:
            sys.exit(1)

        ssh_res = test_ssh_registration()
        if ssh_res == 0:
            print("aursh-ssh is successfully registered as a builtin")
        else:
            sys.exit(1)

        interpreter_res = test_interpreter_regressions()
        if interpreter_res == 0:
            print("interpreter regressions passed")
        else:
            sys.exit(1)

    else:
        result = test(args)
        if result == 0:
            print("AurSh is functional with argument {}".format(args))
        else:
            sys.exit(1)

