﻿// Copyright 2013 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.

[<AutoOpen>]
module IntelliFactory.Core.TextPipes

open System
open System.IO
open System.Text
open System.Threading
open System.Threading.Tasks
open IntelliFactory.Core

#nowarn "40"

type TextMessage =
    | OnClose
    | OnFlush
    | OnWrite of string
    | OnWriteLine of string

type NonBlockingTextWriterConfig =
    {
        BufferSize : int
        Encoding : Encoding
        FlushInterval : TimeSpan
        NewLine : string
    }

    static member Default =
        {
            BufferSize = 1024
            Encoding = FileSystem.DefaultEncoding
            FlushInterval = TimeSpan.FromSeconds 1.
            NewLine = "\n"
        }

type Conf = NonBlockingTextWriterConfig

module Buffer =

    [<Sealed>]
    type private BufferState(out: TextMessage -> unit, cfg) =
        let b = new StringBuilder(cfg.BufferSize)
        let nl = cfg.NewLine
        let mutable isClosed = false

        let write (t: string) =
            if not isClosed then
                b.Append(t) |> ignore

        let writeLine (t: string) =
            if not isClosed then
                b.Append(t) |> ignore
                b.Append(nl) |> ignore

        let flush () =
            if not isClosed then
                match b.Length with
                | 0 -> ()
                | _ -> OnWrite (b.Reset()) |> out

        let close () =
            if not isClosed then
                flush ()
                isClosed <- true
                out OnClose

        member x.Dispatch msg =
            if not isClosed then
                match msg with
                | OnClose -> close ()
                | OnFlush -> flush ()
                | OnWrite t -> write t
                | OnWriteLine t -> writeLine t

        member x.IsClosed  =
            isClosed

    [<Sealed>]
    type Agent private (m: MailboxProcessor<TextMessage>) =

        static let reset : TimerCallback =
            TimerCallback (fun (x: obj) ->
                let a = x :?> MailboxProcessor<TextMessage>
                a.Post OnFlush)

        member x.Post msg = m.Post msg

        static member Start(cfg, out) =
            let t = cfg.FlushInterval
            let a =
                MailboxProcessor.Start(fun agent ->
                    async {
                        let buf = BufferState(out, cfg)
                        let interval = int t.TotalMilliseconds
                        use timer = new Timer(reset, agent, interval, interval)
                        while not buf.IsClosed do
                            let! msg = agent.Receive()
                            do buf.Dispatch msg
                    })
            Agent a

[<Sealed>]
type FunctionTextWriter(i: TextMessage -> unit, cfg) =
    inherit TextWriter()

    static let task x = Task.FromResult x :> Task
    let enc = cfg.Encoding

    override w.Close() = i OnClose

    override w.Write(data: char) = i (OnWrite (string data))
    override w.Write(data: char[]) = i (OnWrite (String data))
    override w.Write(data: string) = i (OnWrite data)
    override w.Write(data: char[], offset: int, count: int) = i (OnWrite (String(data, offset, count)))

    override w.WriteLine(data: char) = i (OnWriteLine (string data))
    override w.WriteLine(data: char[]) = i (OnWriteLine (String data))
    override w.WriteLine(data: string) = i (OnWriteLine data)
    override w.WriteLine(data: char[], offset: int, count: int) = i (OnWriteLine (String(data, offset, count)))

    #if NET40
    #else
    override w.WriteAsync(data: char) = w.Write(data); task ()
    override w.WriteAsync(data: string) = w.Write(data); task ()
    override w.WriteAsync(d, o, c) = w.Write(d, o, c); task c
    override w.WriteLineAsync(data: char) = w.WriteLine(data); task ()
    override w.WriteLineAsync(data: string) = w.WriteLine(data); task ()
    override w.WriteLineAsync(d, o, c) = w.WriteLine(d, o, c); task c
    #endif

    override w.Encoding = enc

    override w.Flush() = i OnFlush

    #if NET40
    #else
    override w.FlushAsync() = i OnFlush; task ()
    #endif

[<Sealed>]
type NonBlockingTextWriter =

    static member Create(out: string -> unit, ?config) =
        let cfg = defaultArg config Conf.Default
        let output msg =
            match msg with
            | OnWrite t -> out t
            | OnClose -> out ""
            | _ -> ()
        let agent = Buffer.Agent.Start(cfg, output)
        new FunctionTextWriter(agent.Post, cfg) :> TextWriter

[<AutoOpen>]
module TextPipes =

    type Buf = ArraySegment<char>

    type ReadCont =
        {
            OnDone : int -> unit
            Buf : Buf
        }

    type Message =
        | OnDone
        | OnRead of ReadCont
        | OnWrite of string

    type PipeState =
        | PipeEmpty
        | PipeFull
        | PipeWaiting of ReadCont

    [<Sealed>]
    type PipeAgent() =

        let b = StringBuilder(1024)

        let emptyBuf r =
            let k = b.Dequeue r.Buf
            Async.Spawn(r.OnDone, k)
            match b.Length with
            | 0 -> PipeEmpty
            | _ -> PipeFull

        member a.Read r st =
            match st with
            | PipeEmpty -> PipeWaiting r
            | PipeFull -> emptyBuf r
            | PipeWaiting q -> failwith "TextPipe can serve only one reader"

        member a.WriteString (data: string) st =
            if data.Length > 0 then
                b.Append(data) |> ignore
                match st with
                | PipeEmpty | PipeFull -> PipeFull
                | PipeWaiting r -> emptyBuf r
            else st

        member a.Done st =
            match st with
            | PipeEmpty -> ()
            | PipeFull -> ()
            | PipeWaiting r ->
                Async.Spawn(r.OnDone, 0)

    let startAgent () =
        MailboxProcessor.Start(fun self ->
            let a = PipeAgent()
            let rec loop st =
                async {
                    let! msg = self.Receive()
                    match msg with
                    | OnRead r -> return! loop (a.Read r st)
                    | OnWrite s -> return! loop (a.WriteString s st)
                    | OnDone -> return a.Done st
                }
            loop PipeEmpty)

    type Agent = MailboxProcessor<Message>

    [<Sealed>]
    type PipeReader(agent: Agent) =
        inherit TextReader()
        let mutable closed = false

        override x.Close() =
            closed <- true
            agent.Post OnDone

        override x.Read() =
            if closed then -1 else
                let buf = Array.zeroCreate 1
                match x.ReadAsync(buf, 0, 1).Result with
                | 0 -> -1
                | _ -> int buf.[0]

        override x.Read(buf, pos, ct) =
            if closed then 0 else
                x.ReadAsync(buf, pos, ct).Result

        #if NET40
        #else
        override x.ReadAsync(buf, pos, ct) =
            if closed then Task.FromResult 0 else
                Async.FromContinuations(fun (ok, _, _) ->
                    Message.OnRead {
                        OnDone = ok
                        Buf = Buf(buf, pos, ct)
                    }
                    |> agent.Post)
                |> Async.StartAsTask
        #endif

[<Sealed>]
type TextPipe private (cfg: Conf) =
    let agent = startAgent ()
    let r = new PipeReader(agent) :> TextReader

    let send s =
        match s with
        | "" -> agent.Post OnDone
        | _ -> agent.Post(OnWrite s)

    let w = NonBlockingTextWriter.Create(send, cfg)

    member x.Close() =
        agent.Post OnDone
        w.Close()

    member x.Reader = r
    member x.Writer = w

    static member Create(?config: Conf) =
        TextPipe(defaultArg config Conf.Default)

