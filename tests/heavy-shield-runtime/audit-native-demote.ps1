$ErrorActionPreference='Stop'
$gemNative='E:/Kingdom.Two.Crowns.Call.of.Olympus/Kingdom.Two.Crowns.Build.22992091'
Add-Type -Path ($gemNative+'/BepInEx/core/Iced.dll')
$gemBytes=[IO.File]::ReadAllBytes($gemNative+'/GameAssembly.dll')
$gemPe=[BitConverter]::ToInt32($gemBytes,60)
$gemSections=$gemPe+24+[BitConverter]::ToUInt16($gemBytes,$gemPe+20)
$gemSectionCount=[BitConverter]::ToUInt16($gemBytes,$gemPe+6)
$gemReport=[Collections.Generic.List[string]]::new()
foreach($gemRequest in @(@('Character.OnEnable',0x980030,0x660),@('Character.HandleOnReceiveDamage',0x97f2c0,0x900),@('Character.Demote',0x97e520,0x2a0))) {
    $gemRva=[int]$gemRequest[1]
    for($gemIndex=0;$gemIndex -lt $gemSectionCount;$gemIndex++) {
        $gemSection=$gemSections+40*$gemIndex
        $gemVA=[BitConverter]::ToInt32($gemBytes,$gemSection+12)
        $gemSize=[BitConverter]::ToInt32($gemBytes,$gemSection+16)
        if($gemRva -ge $gemVA -and $gemRva -lt $gemVA+$gemSize) {
            $gemRaw=[BitConverter]::ToInt32($gemBytes,$gemSection+20)+$gemRva-$gemVA
            break
        }
    }
    $gemBuffer=New-Object byte[] ([int]$gemRequest[2])
    [Array]::Copy($gemBytes,$gemRaw,$gemBuffer,0,$gemBuffer.Length)
    $gemDecoder=[Iced.Intel.Decoder]::Create(64,$gemBuffer,[ulong](0x180000000L+$gemRva),[Iced.Intel.DecoderOptions]::None)
    $gemReport.Add([string]$gemRequest[0])
    while($gemDecoder.IP -lt 0x180000000L+$gemRva+$gemBuffer.Length) {
        $gemInst=$gemDecoder.Decode()
        $gemReport.Add(('{0:X16}: {1}' -f $gemInst.IP,$gemInst.ToString()))
        if($gemInst.Mnemonic.ToString() -eq 'Ret') {break}
    }
}
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'native-demote-calls.txt'),$gemReport,[Text.UTF8Encoding]::new($false))
$gemReport | Where-Object {$_ -match '^Character|call.*1809BDFC0|call.*18097E520|call.*1809814C0|call.*1806BFB90|ret'}
