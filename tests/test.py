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

# Function to test if AurSh is Ok
def test(args=None):

    if not args:
        print("testing AurSh...")
        time.sleep(4)
        # We need to capture the exit code (e.g: 0,1, ...)
        Executed = subprocess.run([AURSH_BIN, "-c", "echo ok"], capture_output=True, text=True)
    else:
        print("testing AurSh...")
        time.sleep(4)
        # Pass elements of args as individual list items
        Executed = subprocess.run([AURSH_BIN,"-c"] + args, capture_output=True, text=True)

    return Executed.returncode

def test_ssh_registration():
    print("testing aursh-ssh registration...")
    time.sleep(1)
    Executed = subprocess.run([AURSH_BIN, "-c", "type aursh-ssh"], capture_output=True, text=True)
    return Executed.returncode

if(__name__=="__main__"):
    args = sys.argv[1:]

    if len(args) == 0: # test with echo to see if aursh is ok
        res = test()
        if res == 0:
            print("AurSh is functional")
        else:
            print("AurSh failed to run")
            sys.exit(1)

        ssh_res = test_ssh_registration()
        if ssh_res == 0:
            print("aursh-ssh is successfully registered as a builtin")
        else:
            print("aursh-ssh registration test failed")
            sys.exit(1)

    else:
        # Pass the entire array of arguments over
        result = test(args)
        if result == 0:
            print("AurSh is functional with argument {}".format(args))
        else:
            print("AurSh exited with code: {}".format(args))
            sys.exit(1)
