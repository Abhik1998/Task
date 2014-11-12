﻿// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open System
open System.Net
open Cricket
open PingPong

ActorHost.Start()
         .SubscribeEvents(fun (evnt:ActorEvent) -> printfn "%A" evnt)
         .EnableRemoting(
               [new TCPTransport(TcpConfig.Default(IPEndPoint.Create(12002)))],
               new BinarySerializer(),
               new TcpActorRegistryTransport(TcpConfig.Default(IPEndPoint.Create(12003))),
               new UdpActorRegistryDiscovery(UdpConfig.Default(), 1000)
         ) |> ignore

let ping count =
    actor {
        name "ping"
        body (
                let pong = !~"pong"
                let rec loop count = messageHandler {
                    let! msg = Message.receive()
                    match msg with
                    | Pong when count > 0 ->
                          if count % 1000 = 0 then printfn "Ping: pong %d" count
                          do! Message.post pong Ping
                          return! loop (count - 1)
                    | Ping -> failwithf "Ping: received a ping message, panic..."
                    | _ -> do! Message.post pong Stop
                }
                
                loop count        
           ) 
    }

[<EntryPoint>]
let main argv = 
    Console.WriteLine("Press enter to start")
    Console.ReadLine() |> ignore

    let pingRef = Actor.spawn (ping 100000)
    pingRef <-- Pong

    Console.WriteLine("Press enter to exit")
    Console.ReadLine() |> ignore
    ActorHost.Dispose()

    Console.WriteLine("Shutdown")
    Console.ReadLine() |> ignore
    0 // return an integer exit code
