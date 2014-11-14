﻿namespace Cricket 

open System
open System.Threading
open Cricket
open Cricket.Diagnostics

#if INTERACTIVE
open Cricket
open Cricket.Diagnostics
#endif

type InvalidMessageException(payload:obj, innerEx:Exception) =
    inherit Exception("Unable to handle msg", innerEx)
    member val Buffer = payload with get

type MessageHandler<'a, 'b> = MH of ('a -> Async<'b>)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Message = 
    
    let private traceHandled (context:ActorCell<_>) =
        Trace.write (TraceEntry.Create(context.Self.Path, 
                               context.Sender.Path, 
                               "message_handled",
                               ?parentId = context.ParentId, 
                               spanId = context.SpanId))

    let private traceReceive (context:ActorCell<_>) =
        Trace.write (TraceEntry.Create(context.Self.Path, 
                               context.Sender.Path, 
                               "message_receive",
                               ?parentId = context.ParentId, 
                               spanId = context.SpanId))

    let map f (msg:Message<'a>) : Message<'b> = 
        {
          Id = msg.Id
          Sender = msg.Sender
          Message = (f msg.Message)
        }

    let create<'a> sender (payload:'a) : Message<'a> = 
        { Id = None; Message = payload; Sender = (defaultArg sender Null) }
    
    let receive() = MH (fun (ctx:ActorCell<_>) -> async {
        let! msg = ctx.Mailbox.Receive()
        ctx.Sender <- msg.Sender
        ctx.ParentId <- msg.Id
        ctx.SpanId <- Random.randomLong()
        traceReceive ctx
        return msg.Message  
    })
    
    let sender() = MH (fun (ctx:ActorCell<_>) -> async { return ActorSelection.op_Implicit ctx.Sender }) 

    let context() = MH (fun (ctx:ActorCell<_>) -> async { return ctx }) 

    let tryReceive timeout = MH (fun (ctx:ActorCell<_>) -> async {
        let! msg = ctx.Mailbox.TryReceive(timeout)
        return Option.map (fun (msg:Message<_>)-> 
            ctx.Sender <- msg.Sender
            ctx.ParentId <- msg.Id
            ctx.SpanId <- Random.randomLong()
            traceReceive ctx 
            msg.Message) msg 
    })

    let scan f = MH (fun (ctx:ActorCell<_>) -> async {
        let! (msg:Message<_>) = ctx.Mailbox.Scan(f)
        ctx.Sender <- msg.Sender
        ctx.ParentId <- msg.Id
        ctx.SpanId <- Random.randomLong()
        traceReceive ctx
        return msg.Message  
    })

    let tryScan timeout f = MH (fun (ctx:ActorCell<_>) -> async {
        let! msg = ctx.Mailbox.TryScan(timeout, f)
        return Option.map (fun (msg:Message<_>) -> 
            ctx.Sender <- msg.Sender
            ctx.ParentId <- msg.Id
            ctx.SpanId <- Random.randomLong()
            traceReceive ctx
            msg.Message) msg
    })

    let postMessage (targets:'a) msg =
        (ActorSelection.op_Implicit targets).Refs 
        |> List.iter (fun target ->
                        match target with
                        | ActorRef(target) -> target.Post(map box msg)
                        | Null -> ())

    let post targets msg =
        MH (fun (ctx:ActorCell<_>) -> async {
                do postMessage targets { Id = Some ctx.SpanId; Sender = ctx.Self; Message = msg }
            }
        )

    let reply msg = MH (fun (ctx:ActorCell<_>) -> async {
        let targets = (ActorSelection([ctx.Sender]))
        do postMessage targets { Id = ctx.ParentId; Sender = ctx.Self; Message = msg }
    })

    type MessageHandlerBuilder() = 
        member __.Bind(MH handler,f) =
             MH (fun context -> 
                  async {
                     let! comp = handler context
                     let (MH nextComp) = f comp
                     return! nextComp context
                  } 
             ) 
        member __.Bind(a:Async<_>, f) = 
            MH (fun context -> 
                async {
                     let! comp = a
                     let (MH nextComp) = f comp
                     return! nextComp context
                  } 
            )

        member __.Return(m) = 
            MH(fun ctx -> 
                traceHandled ctx; 
                async { return m } )

        member __.ReturnFrom(MH m) = 
            MH(fun ctx -> 
                traceHandled ctx; 
                m(ctx))

        member __.Zero() = MH(fun _ -> async.Zero())
        member x.Delay(f) = x.Bind(x.Zero(), f)
        
        member x.Combine(c1 : MessageHandler<_,_>, c2) = x.Bind(c1, fun () -> c2)

        member __.Using(r,f) = MH(fun ctx -> use rr = r in let (MH g) = f rr in g ctx)

        member x.TryFinally(MH body, comp) = 
            MH(fun ctx -> async.TryFinally(body ctx, comp))

        member x.TryWith(MH body, comp) =
            MH(fun ctx -> 
                let comp' = 
                    (fun err -> 
                        let (MH comp) = comp err
                        comp ctx
                    )
                async.TryWith(body ctx, comp')) 

        member x.For(sq:seq<'a>, f:'a -> MessageHandler<'b, 'c>) = 
          let rec loop (en:System.Collections.Generic.IEnumerator<_>) = 
            if en.MoveNext() then x.Bind(f en.Current, fun _ -> loop en)
            else x.Zero()
          x.Using(sq.GetEnumerator(), loop)

        member x.While(t, f:unit -> MessageHandler<_, unit>) =
          let rec loop () = 
            if t() then x.Bind(f(), loop)
            else x.Zero()
          loop()

module MessageHandler = 
    
    let toAsync ctx (MH handler) = handler ctx |> Async.Ignore

    let empty = 
        (MH (fun _ -> 
         let rec loop() = 
             async { return! loop() }
         loop()))

                
