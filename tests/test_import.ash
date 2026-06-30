let my_mod = import("math_module.ash")
echo "Module loaded!"

let msg = my_mod.shared_var
echo "Shared var from module: $msg"

let result = my_mod.math_add(10, 20)
echo "Result of 10 + 20: $result"
