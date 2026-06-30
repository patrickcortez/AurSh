try {
    echo "Running dangerous operation..."
    throw "Database Connection Failed!"
    echo "This should never print!"
} catch e {
    echo "Caught an error natively!"
    echo "Error object: $e"
}
echo "Script recovered successfully!"
