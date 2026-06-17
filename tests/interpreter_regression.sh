users=(guest admin bob)
count=0

for u in ${users[@]}; do
    if [ "$u" = "admin" ]; then
        echo "Found admin! Breaking early."
        break
    else
        echo "Processing $u..."
    fi
done

while [ "$count" -lt 3 ]; do
    if [ "$count" -eq 1 ]; then
        count=$(( count + 1 ))
        continue
    fi
    echo "While loop count: $count"
    count=$(( count + 1 ))
done

for outer in 1 2; do
    for inner in a b; do
        echo "Nested $outer/$inner"
        continue 2
    done
    echo "Skipped inner tail"
done

pipe_res=$(echo "apple banana cherry" | grep "banana")
echo "Grep result: $pipe_res"
