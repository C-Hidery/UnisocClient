Imports System
Imports System.CodeDom
Imports System.Drawing
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading
Imports SPRDClientCore
Imports SPRDClientCore.Models
Imports SPRDClientCore.Protocol
Imports SPRDClientCore.Protocol.Encoders
Imports SPRDClientCore.Utils
Module Program
    Private utils As SprdFlashUtils
    Private CoreVer = "1.3.0.0"
    Public timeout_w As Integer = 30
    Private sprd4 As Boolean = False
    'Private sprd_mode As String
    'Private timeout_o = 100000
    Private kickmode As Integer
    Public connected As Boolean = False
    Public FDL1_Loaded As Boolean = False
    Private kickp As (method As MethodOfChangingDiagnostic, diag As ModeToChange)
    Public isReconnecting = False
    Public FDL2_Loaded As Boolean = False
    Public FDL2_Executed As Boolean = False
    Private device_stage As (SprdMode As Stages, Stage As Stages)
    Private Exit_need As Boolean = False
    Private isSprd4NoFDL As Boolean = False
    Private isSprd4NoFDLMode As Boolean = False
    Private cts As New CancellationTokenSource
    Private bar As New ConsoleProgressBar
    Private exec_addr_en As Boolean = False
    Private exec_addr As ULong
    Private exec_path As String
    Private config_boot As Boolean = False
    Private partitions As (partition As List(Of Partition), method As GetPartitionsMethod)
    Private getpart As Boolean = False
    Private ini As IniFileReader
    Private ReadOnly commandHelp As String = $"{Environment.NewLine}Help:{Environment.NewLine}Options:{Environment.NewLine}--kick{Environment.NewLine}     Kick device into Brom mode.{Environment.NewLine}--kickto{Environment.NewLine}     Kick device to a customized mode, supported value: 1-127{Environment.NewLine}--wait{Environment.NewLine}     Set timeout value for connecting device.{Environment.NewLine}--nofdl{Environment.NewLine}     When device is in SPRD4, it may not need FDL, you can set this option to skip Brom/FDL1 stage.{Environment.NewLine}-c/--config{Environment.NewLine}     Boot device through a configuration.Please set up the config file before using.{Environment.NewLine}Commands:{Environment.NewLine}->Send flash download layer (FDL) file:{Environment.NewLine}     fdl [FILE PATH] [SEND ADDR]{Environment.NewLine}->Flash a partition:{Environment.NewLine}     flash/w [PARTITION NAME] [IMAGE FILE PATH]{Environment.NewLine}->Read a partition:{Environment.NewLine}     read/r [PARTITION NAME] <SAVE PATH> <READ SIZE(KB)> <READ OFFSET>{Environment.NewLine}->Read all partitions (except Userdata){Environment.NewLine}     read_all{Environment.NewLine}->Erase a partition:{Environment.NewLine}     erase/e [PARTITION NAME]{Environment.NewLine}->Erase all partitions{Environment.NewLine}     erase_all{Environment.NewLine}->Get partition size:{Environment.NewLine}     part_size/ps [PARTITION NAME]{Environment.NewLine}->Check a partition if exist:{Environment.NewLine}     check_part/cp [PARTITION NAME]{Environment.NewLine}->Power off device:{Environment.NewLine}     poweroff/exit{Environment.NewLine}->Reboot device (to customized mode):{Environment.NewLine}     reboot/reset <MODE>{Environment.NewLine}     Supported mode: recovery/fastboot/factory_reset{Environment.NewLine}->Repartition:{Environment.NewLine}     repartition/repart [XML file]{Environment.NewLine}->Save partition list for repartition(XML):{Environment.NewLine}     save_xml{Environment.NewLine}->Use CVE to skip FDL file verification(Brom stage only):{Environment.NewLine}     fdl_off_addr [BINARY FILE PATH] [ADDR]{Environment.NewLine}->Set active slot:{Environment.NewLine}     set_active [SLOT]{Environment.NewLine}->Set dm-verify status:{Environment.NewLine}     verify [0,1]{Environment.NewLine}->Print partition table:{Environment.NewLine}     print/p{Environment.NewLine}->Write configuration{Environment.NewLine}     config{Environment.NewLine}->Unlock Bootloader{Environment.NewLine}     unlock{Environment.NewLine}->Lock Bootloader{Environment.NewLine}     lock{Environment.NewLine}->Send a specified packet to device{Environment.NewLine}     send_pak/send [PACKETS]{Environment.NewLine}Additional Notes:{Environment.NewLine}fdl_off_addr : send a binary file to the specified memory address to bypass the signature verification by brom for splloader/fdl1.Used for CVE-2022-38694.{Environment.NewLine}unlock/lock : It is only supported on special FDL2 and requires trustos and sml partition files."
    Sub Main(args As String())
        If Not File.Exists("config.ini") Then
            Using File.Create("config.ini")
            End Using
            File.WriteAllText("config.ini", $"[Config]{Environment.NewLine}fdl_1_path={Environment.NewLine}fdl_1_addr={Environment.NewLine}fdl_2_path={Environment.NewLine}fdl_2_addr={Environment.NewLine}")
        End If
        Console.WriteLine("UnisocClient Version 1.2.2.0")
        Console.WriteLine("Copyright Ryan Crepa - All Rights Reserved")
        Console.WriteLine("Core by YC")
        Console.WriteLine($"Core Version : {CoreVer}")

        Dim i As Integer = 0
        While i < args.Length
            Select Case args(i)
                Case "--kick"
                    sprd4 = True
                    'sprd_mode = "1"
                    i += 1
                    kickp.method = MethodOfChangingDiagnostic.CommonMode
                    kickp.diag = ModeToChange.DlDiagnostic
                Case "--kickto"
                    sprd4 = True
                    Dim mode As Byte
                    If i < args.Length AndAlso Byte.TryParse(args(i + 1), mode) Then
                        kickp.method = MethodOfChangingDiagnostic.CommonMode
                        kickp.diag = CType(mode, ModeToChange)
                    Else
                        DEG_LOG("Kick Mode needed.", "E")
                        Exit Sub
                    End If
                Case "--wait"
                    If i + 1 < args.Length Then
                        Dim nextArg As String = args(i + 1)
                        If Not nextArg.StartsWith("--") Then
                            If Not Integer.TryParse(nextArg, timeout_w) Then
                                DEG_LOG("Bad wait time value!", "E")
                            End If
                            i += 2
                        Else
                            DEG_LOG("Empty value for wait time", "W")
                            i += 1
                        End If
                    Else
                        DEG_LOG("Empty value for wait time", "W")
                        i += 1
                    End If
                Case "--nofdl"

                    Try

                        'DEG_LOG("Sprd4 No FDL mode e")
                        isSprd4NoFDLMode = True
                        i += 1
                    Catch ex As Exception
                        DEG_LOG($"Can not execute Sprd4 No FDL mode, please execute FDL files : {ex.Message}")
                    End Try
                Case "-c", "--config"
                    config_boot = True
                    i += 1
                Case "-r"
                    If sprd4 Then
                        DEG_LOG("You can't use --kick and -r at the same time!", "E")
                    Else
                        isReconnecting = True
                    End If
                    i += 1
                Case "help", "-h", "--help", "-?", "/?", "/h", "/help"
                    Console.WriteLine(commandHelp)
                    Exit Sub
                Case Else
                    i += 1
            End Select
        End While
        If Not sprd4 Then
            Console.WriteLine($"<waiting for connection,mode:dl,{timeout_w}s>")
            Dim port
            Try
                port = SprdProtocolHandler.FindComPort(timeout:=timeout_w * 1000)
            Catch ex As Exception
                DEG_LOG($"Failed to connect to device: {ex.Message}", "E")
                Exit Sub
            End Try

            DEG_LOG($"Find port: {port}")
            DEG_LOG("Connecting to device...", "OP")
            Dim handler As New SprdProtocolHandler(port, New HdlcEncoder)
            Dim monitor As New ComPortMonitor(port)
            utils = New SprdFlashUtils(handler)
            monitor.SetDisconnectedAction(Sub()
                                              monitor.Stop()
                                              cts.Cancel()
                                              Exit Sub
                                          End Sub)
            AddHandler utils.Log, AddressOf LogInvoke
            AddHandler utils.UpdatePercentage, AddressOf bar.UpdateProgress
            AddHandler utils.UpdateStatus, AddressOf bar.UpdateSpeed
            AddHandler Console.CancelKeyPress, AddressOf CancelKeyPressHandler
            Try

                device_stage = utils.ConnectToDevice()
                DEG_LOG($"{device_stage.Stage.ToString} connected")
                DEG_LOG($"Device mode: {device_stage.Stage.ToString}/{device_stage.SprdMode.ToString}")
                connected = True
                If device_stage.Stage = Stages.Fdl1 Then
                    FDL1_Loaded = True
                End If
                If device_stage.Stage = Stages.Fdl2 Then
                    FDL2_Executed = True
                End If
            Catch ex As Exception
                DEG_LOG($"Falied to connect to device: {ex.Message}", "E")

            End Try
        Else
            If sprd4 Then

                Dim port
                Console.WriteLine($"<waiting for connection,mode:boot/cali,{timeout_w}s>")
                Try
                    port = SprdProtocolHandler.FindComPort(timeout:=timeout_w * 1000)
                Catch ex As Exception
                    DEG_LOG($"Failed to connect to device: {ex.Message}", "E")
                    Exit Sub
                End Try
                'DEG_LOG("Kick device to brom, reconnecting...", "OP")
                Dim handler = New SprdProtocolHandler(port, New HdlcEncoder)
                SprdFlashUtils.ChangeDiagnosticMode(handler,,, kickp.method, kickp.diag)
                Dim monitor As New ComPortMonitor(port)
                utils = New SprdFlashUtils(handler)
                monitor.SetDisconnectedAction(Sub()
                                                  monitor.Stop()
                                                  Exit Sub
                                              End Sub)
                AddHandler utils.Log, AddressOf LogInvoke
                AddHandler utils.UpdatePercentage, AddressOf bar.UpdateProgress
                AddHandler utils.UpdateStatus, AddressOf bar.UpdateSpeed
                Try

                    device_stage = utils.ConnectToDevice()
                    DEG_LOG("BootRom connected")
                    DEG_LOG($"Device mode: {device_stage.Stage.ToString}/{device_stage.SprdMode.ToString}")
                    connected = True
                    If device_stage.Stage = Stages.Fdl1 Then
                        FDL1_Loaded = True
                    End If
                    If device_stage.Stage = Stages.Fdl2 Then
                        FDL2_Executed = True
                    End If
                Catch ex As Exception
                    DEG_LOG($"Falied to connect to device: {ex.Message}", "E")

                End Try
            End If

        End If
        'DEG_LOG(commandHelp)

        Dim EnterText As String
        If connected Then
            ParseArgsCommand(args)
            If isSprd4NoFDLMode Then
                Try
                    utils.ExecuteDataAndConnect(Stages.Brom)
                    utils.ExecuteDataAndConnect(Stages.Fdl1)
                    DEG_LOG("Sprd4 No FDL mode enabled.")
                    isSprd4NoFDL = True
                Catch ex As Exception
                    DEG_LOG("Can not execute device through Sprd4 No FDL mode, please execute FDL files.", "E")
                End Try


            End If
            If config_boot Then
                ini = New IniFileReader("config.ini")
st1:
                Try
                    If Not FDL1_Loaded Then
                        Using fs As Stream = File.OpenRead(ini.GetValue("Config", "fdl_1_path"))
                            utils.SendFile(fs, SprdFlashUtils.StringToSize(ini.GetValue("Config", "fdl_1_addr")))
                        End Using
                        utils.ExecuteDataAndConnect(Stages.Brom)
                        device_stage.Stage = Stages.Fdl1
                        FDL1_Loaded = True
                        GoTo st1
                    ElseIf FDL2_Executed = False Then
                        Using fs As Stream = File.OpenRead(ini.GetValue("Config", "fdl_2_path"))
                            utils.SendFile(fs, SprdFlashUtils.StringToSize(ini.GetValue("Config", "fdl_2_addr")))
                        End Using
                        FDL2_Loaded = True
                        utils.ExecuteDataAndConnect(Stages.Fdl1)
                        device_stage.Stage = Stages.Fdl2
                        FDL2_Executed = True
                        getpart = True
                    End If


                Catch ex As Exception
                    DEG_LOG($"Can not boot device with config mode: {ex.Message}")
                End Try

            End If
            While True

                Console.Write($"[{device_stage.Stage}]: ")
                EnterText = Console.ReadLine()
                If EnterText IsNot Nothing Then
                    ExecuteCommand(EnterText)
                Else
                    ExecuteCommand("")
                End If
                If Exit_need Then
                    cts.Cancel()

                    Exit While
                End If


            End While
        End If


    End Sub
    Private Sub CancelKeyPressHandler(sender As Object, e As ConsoleCancelEventArgs)
        cts.Cancel()
        cts = New CancellationTokenSource()
    End Sub
    Private Async Sub ParseArgsCommand(args As String())
        Dim i = 0
        Try


            While i < args.Length


                Select Case args(i)

                    Case "fdl_off_addr"
                        exec_addr_en = True
                        exec_path = args(i + 1)
                        exec_addr = args(i + 2)
                        DEG_LOG($"Current addr is {exec_addr}")
                        i += 3
                    Case "fdl"
                        i += 1
                        While True
                            If args(i) = "fdl_off_addr" Or args(i) = "fdl" Or args(i) = "w" Or args(i) = "flash" Or args(i) = "erase" Or args(i) = "e" Or args(i) = "verify" Or args(i) = "set_active" Or args(i) = "poweroff" Or args(i) = "exit" Or args(i) = "reset" Or args(i) = "reboot" Or args(i) = "ps" Or args(i) = "part_size" Or args(i) = "check_part" Or args(i) = "cp" Or args(i) = "p" Or args(i) = "print" Or args(i) = "config" Or args(i) = "repart" Or args(i) = "repartition" Or args(i) = "save_xml" Or args(i) = "unlock" Or args(i) = "lock" Or args(i) = "send_pak" Or args(i) = "send" Then
                                Exit While
                            Else
                                Dim path1 = args(i)
                                Dim addr = args(i + 1)
                                Await ExecuteCommand($"fdl ""{path1}"" {addr}")
                                i += 2
                            End If
                        End While
                    Case "flash", "w"
                        Dim name = args(i + 1)
                        Dim image = args(i + 2)
                        Await ExecuteCommand($"flash {name} ""{image}""")
                        i += 3
                    Case "read", "r"
                        Dim name = args(i + 1)
                        If i + 2 < args.Length Then
                            '有下一个项，判断
                            If args(i + 2) = "fdl_off_addr" Or args(i + 2) = "fdl" Or args(i + 2) = "w" Or args(i + 2) = "flash" Or args(i + 2) = "erase" Or args(i + 2) = "e" Or args(i + 2) = "verify" Or args(i + 2) = "set_active" Or args(i + 2) = "poweroff" Or args(i + 2) = "exit" Or args(i + 2) = "reset" Or args(i + 2) = "reboot" Or args(i + 2) = "ps" Or args(i + 2) = "part_size" Or args(i + 2) = "check_part" Or args(i + 2) = "cp" Or args(i + 2) = "p" Or args(i + 2) = "print" Or args(i + 2) = "config" Or args(i + 2) = "repart" Or args(i + 2) = "repartition" Or args(i + 2) = "save_xml" Or args(i) = "unlock" Or args(i) = "lock" Or args(i) = "send_pak" Or args(i) = "send" Then
                                '不是下一个需要的，执行name
                                Await ExecuteCommand($"r {name}")
                                i += 2
                            Else
                                '都不是？那可能是需要的，收了
                                Dim path = args(i + 2)
                                '继续判断有没有size
                                If i + 3 < args.Length Then
                                    '有，判断！
                                    If args(i + 3) = "fdl_off_addr" Or args(i + 3) = "fdl" Or args(i + 3) = "w" Or args(i + 3) = "flash" Or args(i + 3) = "erase" Or args(i + 3) = "e" Or args(i + 3) = "verify" Or args(i + 3) = "set_active" Or args(i + 3) = "poweroff" Or args(i + 3) = "exit" Or args(i + 3) = "reset" Or args(i + 3) = "reboot" Or args(i + 3) = "ps" Or args(i + 3) = "part_size" Or args(i + 3) = "check_part" Or args(i + 3) = "cp" Or args(i + 3) = "p" Or args(i + 3) = "print" Or args(i + 3) = "config" Or args(i + 3) = "repart" Or args(i + 3) = "repartition" Or args(i + 3) = "save_xml" Or args(i) = "unlock" Or args(i) = "lock" Or args(i) = "send_pak" Or args(i) = "send" Then
                                        '你不是需要的，执行path
                                        Await ExecuteCommand($"r {name} ""{path}""")
                                        i += 3
                                    Else
                                        '有size！
                                        Dim size = args(i + 3)
                                        '继续判断有没有offset
                                        If i + 4 < args.Length Then
                                            '有下一个，判断！
                                            If args(i + 4) = "fdl_off_addr" Or args(i + 4) = "fdl" Or args(i + 4) = "w" Or args(i + 4) = "flash" Or args(i + 4) = "erase" Or args(i + 4) = "e" Or args(i + 4) = "verify" Or args(i + 4) = "set_active" Or args(i + 4) = "poweroff" Or args(i + 4) = "exit" Or args(i + 4) = "reset" Or args(i + 4) = "reboot" Or args(i + 4) = "ps" Or args(i + 4) = "part_size" Or args(i + 4) = "check_part" Or args(i + 4) = "cp" Or args(i + 4) = "p" Or args(i + 4) = "print" Or args(i + 4) = "config" Or args(i + 4) = "repart" Or args(i + 4) = "repartition" Or args(i + 4) = "save_xml" Or args(i) = "unlock" Or args(i) = "lock" Or args(i) = "send_pak" Or args(i) = "send" Then
                                                '你不是需要的，执行size
                                                Await ExecuteCommand($"r {name} ""{path}"" {size}")
                                                i += 4
                                            Else
                                                '有offset
                                                Dim offset = args(i + 4)
                                                Await ExecuteCommand($"r {name} ""{path}"" {size} {offset}")
                                            End If
                                        Else
                                            '没了，执行size
                                            Await ExecuteCommand($"r {name} ""{path}"" {size}")
                                            i += 4
                                        End If
                                    End If
                                Else
                                    '哦，没了，执行path
                                    Await ExecuteCommand($"r {name} ""{path}""")
                                    i += 3
                                End If
                            End If
                        Else
                            '没有下一个项
                            Await ExecuteCommand($"r {name}")
                            i += 2
                        End If


                    Case "erase", "e"
                        Dim name = args(i + 1)
                        Await ExecuteCommand($"erase {name}")
                        i += 2
                    Case "erase_all"
                        Await ExecuteCommand("erase_all")
                    Case "ps", "part_size"
                        Await ExecuteCommand("ps")
                        i += 1
                    Case "cp", "check_part"
                        Await ExecuteCommand("cp")
                        i += 1
                    Case "set_active"
                        Dim slot = args(i + 1)
                        Await ExecuteCommand($"set_active {slot}")
                        i += 2
                    Case "verify"
                        Dim status = args(i + 1)
                        Await ExecuteCommand($"verify {status}")
                        i += 2
                    Case "save_xml"
                        Await ExecuteCommand("save_xml")
                        i += 1
                    Case "p", "print"
                        Await ExecuteCommand("print")
                        i += 1
                    Case "repart", "repartition"
                        Dim xml = args(i + 1)
                        Await ExecuteCommand($"repart ""{xml}""")
                        i += 2
                    Case "exit", "poweroff"
                        Await ExecuteCommand("exit")
                    Case "reboot", "reset"
                        If i + 1 < args.Length Then
                            If args(i + 1) = "fdl_off_addr" Or args(i + 1) = "fdl" Or args(i + 1) = "w" Or args(i + 1) = "flash" Or args(i + 1) = "erase" Or args(i + 1) = "e" Or args(i + 1) = "verify" Or args(i + 1) = "set_active" Or args(i + 1) = "poweroff" Or args(i + 1) = "exit" Or args(i + 1) = "reset" Or args(i + 1) = "reboot" Or args(i + 1) = "ps" Or args(i + 1) = "part_size" Or args(i + 1) = "check_part" Or args(i + 1) = "cp" Or args(i + 1) = "p" Or args(i + 1) = "print" Or args(i + 1) = "config" Or args(i + 1) = "repart" Or args(i + 1) = "repartition" Or args(i + 1) = "save_xml" Or args(i) = "unlock" Or args(i) = "lock" Or args(i) = "send_pak" Or args(i) = "send" Then
                                Await ExecuteCommand("reset")
                            Else
                                Dim mode = args(i + 1)
                                Await ExecuteCommand($"reset {mode}")
                            End If
                        Else
                            Await ExecuteCommand("reset")
                        End If
                    Case "read_all"
                        Await ExecuteCommand("read_all")
                        i += 1
                    Case "unlock"
                        Await ExecuteCommand("unlock")
                    Case "lock"
                        Await ExecuteCommand("lock")
                    Case "send_pak", "send"
                        If i + 1 < args.Length Then
                            i += 1
                            While True
                                If args(i) = "fdl_off_addr" Or args(i) = "fdl" Or args(i) = "w" Or args(i) = "flash" Or args(i) = "erase" Or args(i) = "e" Or args(i) = "verify" Or args(i) = "set_active" Or args(i) = "poweroff" Or args(i) = "exit" Or args(i) = "reset" Or args(i) = "reboot" Or args(i) = "ps" Or args(i) = "part_size" Or args(i) = "check_part" Or args(i) = "cp" Or args(i) = "p" Or args(i) = "print" Or args(i) = "config" Or args(i) = "repart" Or args(i) = "repartition" Or args(i) = "save_xml" Or args(i) = "unlock" Or args(i) = "lock" Or args(i) = "send_pak" Or args(i) = "send" Then
                                    Exit While
                                Else
                                    Dim pak = args(i)
                                    Await ExecuteCommand($"send {pak}")
                                    i += 1
                                End If
                            End While
                        End If

                    Case Else
                        i += 1
                End Select
            End While
        Catch ex As Exception
            DEG_LOG($"Failed: Command may incorrect: {ex.Message}", "E")
        End Try
    End Sub
    Private Async Function ExecuteCommand(cmd As String) As Task
        Dim args = ParseCommand(cmd)
        If args.Count > 0 Then
            Select Case args(0)
                Case "fdl"
                    If isSprd4NoFDL Then
                        DEG_LOG("Sprd4 No FDL mode is not support.", "W")
                        Exit Function
                    End If
                    If device_stage.Stage = Stages.Fdl2 Then
                        DEG_LOG("FDL2 is already executed, skipped.", "W")
                        FDL2_Executed = True
                        Exit Function
                    End If
                    If args.Count < 3 Then
                        DEG_LOG("Command incorrect, you may see command help by typing 'help'", "E")
                    Else
                        Dim o = 1
                        Dim execd = False
                        While o < args.Count

                            If Not File.Exists(args(o)) Then
                                DEG_LOG("File does not exist.", "E")
                            Else
                                Try
                                    Using fs As Stream = File.OpenRead(args(o))
                                        utils.SendFile(fs, SprdFlashUtils.StringToSize(args(o + 1)))
                                        If device_stage.Stage = Stages.Brom Then
                                            If exec_addr_en AndAlso execd = False Then
                                                Using ds As Stream = File.OpenRead(exec_path)
                                                    utils.SendFile(ds, SprdFlashUtils.StringToSize(exec_addr))
                                                End Using
                                                execd = True
                                            End If
                                        End If

                                    End Using
                                    DEG_LOG("Send FDL file successfully")
                                Catch ex As Exception
                                    DEG_LOG($"Can not send FDL to {args(o + 1)}: {ex.Message}", "E")
                                    'Exit Sub
                                    Exit_need = True
                                End Try


                            End If
                            o += 3
                        End While

                        Try
                            If FDL1_Loaded = False Then
                                utils.ExecuteDataAndConnect(device_stage.Stage)
                                device_stage.Stage = Stages.Fdl1
                                FDL1_Loaded = True
                                DEG_LOG("Execute FDL1 successfully")
                            ElseIf FDL2_Executed = False Then
                                FDL2_Loaded = True
                                utils.ExecuteDataAndConnect(device_stage.Stage)
                                device_stage.Stage = Stages.Fdl2
                                FDL2_Executed = True
                                DEG_LOG("Execute FDL2 successfully")
                                getpart = True
                            End If

                        Catch ex As Exception
                            DEG_LOG($"Can not execute FDL: {ex.Message}", "E")
                            'Exit Sub
                            Exit_need = True
                        End Try
                    End If
                Case "flash", "w"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        Try
                            If File.Exists(args(2)) And args.Count >= 3 Then
                                Using fs As Stream = File.OpenRead(args(2))
                                    Await utils.WritePartitionAsync(args(1), fs, cts.Token)
                                End Using
                            Else
                                DEG_LOG("File does not exist.", "E")
                                Exit Function
                            End If

                        Catch ex As Exception
                            DEG_LOG($"Can not flash partition {args(1)}: {ex.Message}")
                            Exit_need = True
                        End Try
                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If
                Case "read", "r"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        Try
                            If args.Count >= 3 Then
                                Using fs As Stream = File.Create(args(2))
                                    If args.Count >= 4 Then
                                        If args.Count >= 5 Then
                                            Await utils.ReadPartitionCustomizeAsync(fs, args(1), SprdFlashUtils.StringToSize(args(3)), cts.Token, SprdFlashUtils.StringToSize(args(4)))
                                        Else
                                            Await utils.ReadPartitionCustomizeAsync(fs, args(1), SprdFlashUtils.StringToSize(args(3)), cts.Token)
                                        End If
                                    Else
                                        Await utils.ReadPartitionCustomizeAsync(fs, args(1), utils.GetPartitionSize(args(1)), cts.Token)
                                    End If

                                End Using
                            Else
                                Using fs As Stream = File.Create($"{args(1)}.img")
                                    Await utils.ReadPartitionCustomizeAsync(fs, args(1), utils.GetPartitionSize(args(1)), cts.Token)
                                End Using
                            End If

                        Catch ex As Exception
                            DEG_LOG($"Can not read partition {args(1)}: {ex.Message}")
                        End Try
                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If
                Case "erase", "e"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        If args.Count >= 2 Then
                            Console.Write("Answer 'yes' to confirm operation 'Erase partition':")
                            Dim aw = Console.ReadLine()
                            If aw = "yes" Then
                                Try
                                    utils.ErasePartition(args(1))
                                    DEG_LOG($"Erase partition {args(1)} done.")
                                Catch ex As Exception
                                    DEG_LOG($"Can not erase partition {args(1)}: {ex.Message}")
                                End Try

                            End If
                        Else
                            DEG_LOG("Partition name needed.", "W")
                        End If

                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If
                Case "erase_all"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        Console.Write("Answer 'yes' to confirm operation 'Erase all partitions':")
                        Dim aw = Console.ReadLine()
                        If aw = "yes" Then
                            Try
                                DEG_LOG("Parse partition table...", "OP")
                                partitions = utils.GetPartitionsAndStorageInfo()
                                DEG_LOG("Start to erase all partitions...", "OP")
                                For Each i In partitions.partition
                                    utils.ErasePartition(i.Name)
                                Next
                                DEG_LOG("Erase all partitions done.")
                            Catch ex As Exception
                                DEG_LOG($"Can not erase all partitions: {ex.Message}")
                            End Try
                        End If
                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If
                Case "part_size", "ps"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        If args.Count >= 2 Then
                            Try
                                If utils.CheckPartitionExist(args(1)) Then
                                    DEG_LOG($"{args(1)}: {utils.GetPartitionSize(args(1))}KB")
                                Else
                                    DEG_LOG($"Partition {args(1)} does not exist.", "W")
                                End If

                            Catch ex As Exception
                                DEG_LOG($"Can not get partition size: {ex.Message}")
                            End Try
                        Else
                            DEG_LOG("Partition name needed.", "W")
                        End If

                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If
                Case "check_part", "cp"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        If args.Count >= 2 Then
                            If utils.CheckPartitionExist(args(1)) Then
                                DEG_LOG("Exist.")
                            Else
                                DEG_LOG("Not exist.")
                            End If
                        Else
                            DEG_LOG("Partition name needed.", "W")
                        End If
                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If
                Case "poweroff", "exit"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        utils.ShutdownDevice()
                        Exit_need = True
                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If

                Case "reboot", "reset"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        If args.Count >= 2 Then
                            If args(1) = "recovery" Then
                                utils.ResetToCustomMode(CustomModesToReset.Recovery)
                                utils.PowerOnDevice()
                                Exit_need = True
                            ElseIf args(1) = "fastboot" Then
                                utils.ResetToCustomMode(CustomModesToReset.Fastboot)
                                utils.PowerOnDevice()
                                Exit_need = True
                            ElseIf args(1) = "factory_reset" Then
                                utils.ResetToCustomMode(CustomModesToReset.FactoryReset)
                                utils.PowerOnDevice()
                                Exit_need = True
                            Else
                                DEG_LOG("Unknown mode.", "W")
                            End If
                        Else
                            utils.PowerOnDevice()
                            Exit_need = True
                        End If
                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If
                Case "repartition", "repart"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        If args.Count >= 2 Then
                            If File.Exists(args(1)) Then
                                Try
                                    DEG_LOG("Start to repartition...", "OP")
                                    Dim cont = File.ReadAllText(args(1))
                                    Dim part = SprdFlashUtils.LoadPartitionsXml(cont)
                                    utils.Repartition(part)
                                    DEG_LOG("Command sent.", "OP")
                                Catch ex As Exception
                                    DEG_LOG($"Can not repartition: {ex.Message}")
                                End Try

                            Else
                                DEG_LOG("File does not exist.", "E")
                            End If
                        Else
                            DEG_LOG("XML file needed.", "W")
                        End If
                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If
                Case "save_xml"
                    If FDL2_Executed Or isSprd4NoFDL Then
                        Try
                            DEG_LOG("Parse partition table...", "OP")
                            Dim part_list = utils.GetPartitionsAndStorageInfo()
                            part_list.partitions.RemoveAll(Function(p) p.Name = "splloader")
                            DEG_LOG("Making XML file...", "OP")
                            Using fs As Stream = File.Create($"partition_{DateTime.Now:yyyyMMdd_HHmmss}.xml")
                                SprdFlashUtils.SavePartitionsToXml(part_list.partitions, fs)
                            End Using
                            DEG_LOG("Done!")
                        Catch ex As Exception
                            DEG_LOG($"Can not save XML file: {ex.Message}")
                        End Try
                    Else
                        DEG_LOG("FDL2 not ready.", "W")
                    End If
                Case "fdl_off_addr"
                    If args.Count >= 3 Then
                        exec_addr_en = True
                        exec_addr = SprdFlashUtils.StringToSize(args(2))
                        exec_path = args(1)
                        DEG_LOG($"Current addr is {args(2)}.")
                    Else
                        DEG_LOG("Binary file and addr needed.", "W")
                    End If
                Case "set_active"
                    If args.Count >= 2 Then
                        Try
                            utils.SetActiveSlot(args(1))
                            DEG_LOG($"Current slot is {args(1)}.")
                        Catch ex As Exception
                            DEG_LOG($"Can not set active slot: {ex.Message}", "E")
                        End Try

                    Else
                        DEG_LOG("Acvite slot needed.", "W")
                    End If
                Case "verify"
                    If args.Count >= 2 Then
                        Try
                            utils.SetDmVerityStatus((args(1) = 1), partitions.partition)
                            DEG_LOG($"dm-verify status is {Str((args(1) = 1))}.")
                        Catch ex As Exception
                            DEG_LOG($"Can not set dm-verify status: {ex.Message}")
                        End Try
                    Else
                        DEG_LOG("dm-verify status needed.")
                    End If
                Case "print", "p"
                    DEG_LOG("Parse partition table...", "OP")
                    partitions = utils.GetPartitionsAndStorageInfo()
                    Dim p As Integer = 1
                    For Each i In partitions.partition
                        Dim name = i.Name
                        Dim size = i.Size
                        Console.WriteLine($"{p}. {name} {size}KB")
                        p += 1
                    Next
                Case "help"
                    Console.WriteLine(commandHelp)
                Case "read_all"

                    If FDL2_Executed Or isSprd4NoFDL Then
                        Try
                            DEG_LOG("Parse partition table...", "OP")
                            partitions = utils.GetPartitionsAndStorageInfo()
                            DEG_LOG("Start to read all partition without reading userdata.", "OP")
                            For Each i In partitions.partition
                                If Not i.Name = "userdata" Or i.Name = "data" Then
                                    Using fs As Stream = File.Create($"{i.Name}.img")
                                        Await utils.ReadPartitionCustomizeAsync(fs, i.Name, utils.GetPartitionSize(i.Name), cts.Token)
                                    End Using

                                End If
                            Next
                        Catch ex As Exception
                            DEG_LOG($"Can not read partitions: {ex.Message}", "E")
                        End Try

                    Else
                        DEG_LOG("FDL2 not ready.", "E")
                    End If
                Case "config"
                    ini = New IniFileReader("config.ini")
                    Console.WriteLine("Configuration:")
                    Console.WriteLine($"1.FDL1 file path: {ini.GetValue("Config", "fdl_1_path")}")
                    Console.WriteLine($"2.FDL1 send address: {ini.GetValue("Config", "fdl_1_addr")}")
                    Console.WriteLine($"3.FDL2 file path: {ini.GetValue("Config", "fdl_2_path")}")
                    Console.WriteLine($"4.FDL2 send address: {ini.GetValue("Config", "fdl_2_addr")}")
                    Console.WriteLine("Please select a config to revise.")
                    Dim value = Console.ReadLine()
                    Console.Write("Value:")
                    Dim value2 = Console.ReadLine()
                    Select Case value
                        Case "1"
                            ini.WriteValue("Config", "fdl_1_path", value2)
                        Case "2"
                            ini.WriteValue("Config", "fdl_1_addr", value2)
                        Case "3"
                            ini.WriteValue("Config", "fdl_2_path", value2)
                        Case "4"
                            ini.WriteValue("Config", "fdl_2_addr", value2)
                    End Select
                Case "unlock"
                    Try
                        Dim is1 = utils.SetBootloaderLockStatus(False)
                    Catch ex As Exception
                        DEG_LOG($"Can not set Bootloader status: {ex.Message}")
                    End Try
                Case "lock"
                    Try
                        Dim is1 = utils.SetBootloaderLockStatus(True)
                    Catch ex As Exception
                        DEG_LOG($"Can not set Bootloader status: {ex.Message}")
                    End Try
                Case "send", "send_pak"
                    If args.Count >= 2 Then
                        Dim i = 1
                        While i < args.Count
                            Try
                                Dim raw = CType(args(i), SprdCommand)
                                Dim pak = SprdFlashUtils.StringToSize(raw)
                                Dim bak = utils.Handler.SendPacketAndReceive(pak)
                                DEG_LOG($"Send: {args(i)}, Recv: {bak}", "OP")
                            Catch ex As Exception
                                DEG_LOG($"Failed to send packet: {ex.Message}")
                                Exit While
                            End Try
                            i += 1
                        End While
                    End If
            End Select

        End If
    End Function
    Public Sub LogInvoke(text As String)
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [KERNEL] {text}")
    End Sub



    Private Function ParseCommand(command As String) As List(Of String)
        Dim result As New List(Of String)()
        Dim sb As New StringBuilder()
        Dim inPath As Boolean = False

        For Each c As Char In command
            If c = """"c Then
                inPath = Not inPath
            ElseIf c = " "c AndAlso Not inPath Then
                If sb.Length > 0 Then
                    result.Add(sb.ToString())
                    sb.Clear()
                End If
            Else
                sb.Append(c)
            End If
        Next

        If sb.Length > 0 Then
            result.Add(sb.ToString())
        End If

        Return result
    End Function

    ''' <summary>
    ''' Writeline on window
    ''' </summary>
    ''' <param name="text">Contents</param>
    ''' <param name="info">The type of text.(I,W,E,OP,DE)</param>
    Public Sub DEG_LOG(text As String, Optional info As String = "I")
        If info = "I" Then
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [INFO] {text}")
        ElseIf info = "W" Then
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [WARN] {text}")
        ElseIf info = "E" Then
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [ERROR] {text}")
        ElseIf info = "OP" Then
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [OPERATION] {text}")
        ElseIf info = "DE" Then
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss}] [DEBUG] {text}")
        End If

    End Sub
End Module
Public Class ConsoleProgressBar
    Public Property BarWidth As Integer = 45
    Private _lock As New Object()

    Public Sub UpdateProgress(percentage As Integer)
        If percentage > 100 Then
            percentage = 100
        ElseIf percentage < 0 Then
            percentage = 0
        End If

        Dim progressWidth As Integer = CInt(percentage / 100.0 * BarWidth)
        Dim tmp As ConsoleColor = Console.ForegroundColor
        Console.ForegroundColor = ConsoleColor.Yellow

        SyncLock _lock
            Console.CursorLeft = 0
            Console.Write("["c)
            Console.Write(New String("="c, progressWidth))
            Console.Write(New String(" "c, BarWidth - progressWidth))
            Console.Write("]"c)
            Console.Write($"{percentage}%")
            Console.ForegroundColor = tmp

            If percentage = 100 Then
                Console.WriteLine()
            End If
        End SyncLock
    End Sub

    Public Sub UpdateSpeed(speed As String)
        SyncLock _lock
            Dim tmp As Integer = Console.CursorLeft
            Console.CursorLeft = BarWidth + 10
            Console.Write(speed)
            Console.CursorLeft = tmp
        End SyncLock
    End Sub
End Class
