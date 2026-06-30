function get_message() {
    return "Hello from the new AurSh function system!"
}

function math_test() {
    return 42
}

let msg = get_message()
echo "Function returned: $msg"

let num = math_test()
echo "Math function returned: $num"
