# Read-only. Never outputs BitLocker recovery passwords or changes partitions.
$ErrorActionPreference = 'Stop'
$system = Get-CimInstance Win32_ComputerSystem
$product = Get-CimInstance Win32_ComputerSystemProduct
$report = [ordered]@{
    Manufacturer = $system.Manufacturer
    Model = $system.Model
    Product = $product.Name
    Processor = @(Get-CimInstance Win32_Processor | Select-Object -ExpandProperty Name)
    Partitions = @(Get-Partition | Select-Object DiskNumber, PartitionNumber, Type, Size, DriveLetter)
    Volumes = @(Get-Volume | Select-Object DriveLetter, FileSystem, Size, SizeRemaining)
    BitLocker = @()
    SecureBoot = $null
}
try { $report.SecureBoot = Confirm-SecureBootUEFI } catch { $report.SecureBoot = 'Unknown (run elevated)' }
try {
    $report.BitLocker = @(Get-BitLockerVolume | Select-Object MountPoint, VolumeStatus, ProtectionStatus, EncryptionPercentage)
} catch { $report.BitLocker = @('Unknown (run elevated)') }
$report | ConvertTo-Json -Depth 6
