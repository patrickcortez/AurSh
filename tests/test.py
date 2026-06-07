import subprocess
import sys
import time
import os
import platform

def get_aursh_path():
    ext = ".exe" if platform.system() == "Windows" else ""
    local_bin = os.path.join("bin", f"aursh{ext}")
    if os.path.exists(local_bin):
        return local_bin
    return "aursh"

AURSH_BIN = get_aursh_path()

def test(args=None):
    if not args:
        print("testing AurSh...")
        time.sleep(4)
        Executed = subprocess.run([AURSH_BIN, "-c", "echo ok"], capture_output=True, text=True)
    else:
        print("testing AurSh...")
        time.sleep(4)
        Executed = subprocess.run([AURSH_BIN,"-c"] + args, capture_output=True, text=True)
        
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
    Executed = subprocess.run([AURSH_BIN, "-c", "type aursh-ssh"], capture_output=True, text=True)
    if Executed.returncode != 0:
        print("ERROR: aursh-ssh test failed.")
        print("--- STDOUT ---")
        print(Executed.stdout)
        print("--- STDERR ---")
        print(Executed.stderr)
    return Executed.returncode

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

    else:
        result = test(args)
        if result == 0:
            print("AurSh is functional with argument {}".format(args))
        else:
            sys.exit(1)
