open System
open System.Threading
open System.Windows.Forms
open System.Net
open System.Net.Sockets
open System.Net.NetworkInformation
open System.Reflection
open System.Resources
open System.Drawing

let udpPort = 31211

let myId = Guid.NewGuid ()

[<EntryPoint>]
[<STAThread>]
let main argv = 
    do
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault false

    let resources = new ResourceManager("Resources", Assembly.GetExecutingAssembly())
    
    let icon = resources.GetObject("tray") :?> Icon

    use tray = new NotifyIcon()

    use menu = new ContextMenuStrip()

    use heyMenuItem = new ToolStripMenuItem("Hey!")

    let uiContext = SynchronizationContext.Current

    let localAddress = new IPEndPoint(IPAddress.Any,udpPort)

    use udpServer = new UdpClient(localAddress)

    udpServer.EnableBroadcast <- true
    udpServer.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.Broadcast,1)
    udpServer.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.ReuseAddress,true)
    udpServer.Client.SetSocketOption(SocketOptionLevel.Socket,SocketOptionName.DontRoute,true)

    // Calculate all broadcast address for this machine
    let broadcastAddresses =
        NetworkInterface.GetAllNetworkInterfaces ()
        |> Array.toList
        |> List.filter (fun nic ->
            // Filter out non ethernet / wifi interfaces
            nic.NetworkInterfaceType = NetworkInterfaceType.Ethernet ||
            nic.NetworkInterfaceType = NetworkInterfaceType.Wireless80211    
        )
        |> List.choose (fun nic ->
            // Find the IPv4 address (if any) of the interface
            (nic.GetIPProperties ()).UnicastAddresses |> Seq.tryFind (fun unicastAddress ->
                unicastAddress.Address.AddressFamily = AddressFamily.InterNetwork
            )
        )
        |> List.map (fun unicastAddress ->
            // Transform IPv4 address into broadcast
            let localIPBytes = unicastAddress.Address.GetAddressBytes()
            let netmaskBytes = unicastAddress.IPv4Mask.GetAddressBytes()
            let broadcastBytes = Array.zip localIPBytes netmaskBytes |> Array.map (fun (ip,mask) -> ip ||| (~~~ mask))
            new IPAddress(broadcastBytes)
        )

    let showHey () = do tray.ShowBalloonTip(2500,"Hey!","Listen!",ToolTipIcon.None)

    let sendHey _ =
        do broadcastAddresses |> List.iter(fun broadcastAdress ->
            let payload = myId.ToByteArray ()
            udpServer.Send(payload,payload.Length,new IPEndPoint(broadcastAdress,udpPort)) |> ignore    
        )
 
    let rec receive () = async {
        
        let data = udpServer.Receive (ref localAddress)

        // Prevent echo
        if data <> myId.ToByteArray () then
            do! Async.SwitchToContext uiContext
            do showHey ()
            do! Async.SwitchToThreadPool ()

        do! receive ()
    }

    receive () |> Async.Start    

    do // Setup UI
        heyMenuItem.Click.Add sendHey

        menu.Items.Add heyMenuItem |> ignore

        tray.ContextMenuStrip <- menu
        tray.Visible <- true
        tray.Icon <- icon

    do Application.Run ()

    0
