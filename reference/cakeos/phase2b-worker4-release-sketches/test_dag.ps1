$dag = @{
    nodes = @{}
    edges = @()
}
$key1 = "test"
$key2 = "test2"
$dag.nodes[$key1] = @{productId="test"}
$dag.nodes[$key2] = @{productId="test2"}
Write-Host "Count: " $dag.nodes.Count
Write-Host "Keys.Count: " $dag.nodes.Keys.Count
Write-Host "Keys: " $dag.nodes.Keys