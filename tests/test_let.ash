let x = 5
echo "Global x is: $x"

function test() {
    let x = 10
    echo "Inside test, local x is: $x"
}

test
echo "After test, global x is: $x"
