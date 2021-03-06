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
// permissions and limitations under the License

namespace IntelliFactory.Build

open IntelliFactory.Build
open IntelliFactory.Core
open System
open System.IO
open System.Security
open System.Runtime.CompilerServices
open NuGet

(*

    NOTE: this file contains some code in bizzare and unusual style, including code
    that upcasts and downcasts needlessly. This is caused by the need to satisfy the security
    constraints as discovered by SecAnnotate.exe tool. Currently F# makes all closures
    SecurityTrasparent, therefore closures cannot reference NuGet types that are SecurityCritical,
    unless through indirection (such as boxed values of those types, or through helper methods marked
    SecuritySafeCritical).

*)

/// Implements an IHttpClient that uses provided credentials.
[<SecurityCritical>]
[<Sealed>]
type NuGetConfigHttpClient(log: Log, url, psp: IPackageSourceProvider, cp: ICredentialProvider) =

    let c = HttpClient(url)

    member c.GetCredentialsFromSource(uri: Uri, s: PackageSource) =
        if uri.ToString().StartsWith(s.Source) then
            match cp.GetCredentials(Uri(s.Source), null, CredentialType.RequestCredentials, false) with
            | null -> None
            | cr -> Some cr
        else None

    member c.GetCredentials(uri: Uri) =
        let mutable loop = true
        let mutable result = None
        use enu = psp.GetEnabledPackageSources().GetEnumerator()
        while enu.MoveNext() && loop do
            let s = enu.Current
            for s in psp.GetEnabledPackageSources() do
                match c.GetCredentialsFromSource(uri, s) with
                | None -> ()
                | Some r -> loop <- false; result <- Some r
        result

    interface IProgressProvider with

        [<SecurityCritical>]
        member x.add_ProgressAvailable(h) =
            c.add_ProgressAvailable(h)

        [<SecurityCritical>]
        member x.remove_ProgressAvailable(h) =
            c.add_ProgressAvailable(h)

    interface IHttpClientEvents with

        [<SecurityCritical>]
        member x.add_SendingRequest(h) =
            c.add_SendingRequest(h)

        [<SecurityCritical>]
        member x.remove_SendingRequest(h) =
            c.remove_SendingRequest(h)

    interface IHttpClient with

        [<SecurityCritical>]
        member x.DownloadData(st) = c.DownloadData(st)

        [<SecurityCritical>]
        member x.GetResponse() = c.GetResponse()

        [<SecurityCritical>]
        member x.InitializeRequest(req) =
            c.InitializeRequest(req)
            match x.GetCredentials req.RequestUri with
            | None -> log.Verbose("No credentials for: {0}", req.RequestUri)
            | Some cr ->
                log.Verbose("Authorizing request for {0}", req.RequestUri)
                req.Credentials <- cr

        member x.AcceptCompression
            with [<SecurityCritical>] get () = c.AcceptCompression
            and [<SecurityCritical>] set a = c.AcceptCompression <- a

        member x.OriginalUri
            with [<SecurityCritical>] get () = c.OriginalUri

        member x.UserAgent
            with [<SecurityCritical>] get () = c.UserAgent
            and [<SecurityCritical>] set a = c.UserAgent <- a

        member x.Uri
            with [<SecurityCritical>] get () = c.Uri

[<Sealed>]
[<SecurityCritical>]
type NuGetLogger(env) =
    let log = Log.Create<NuGetLogger>(env)

    interface IFileConflictResolver with

        [<SecurityCritical>]
        member this.ResolveFileConflict(msg) =
            log.Warn("Ignoring file conflicts including: {0}", msg)
            FileConflictResolution.IgnoreAll

    interface ILogger with

        [<SecurityCritical>]
        member this.Log(level, fmt, args) =
            let msg = String.Format(fmt, args)
            match level with
            | MessageLevel.Debug -> log.Verbose msg
            | MessageLevel.Error -> log.Error msg
            | MessageLevel.Info -> log.Info msg
            | _ -> log.Warn msg

[<Sealed>]
[<SecurityCritical>]
type NuGetPackageSourceProviderWrapper(psp: IPackageSourceProvider) =

    interface IPackageSourceProvider with

        [<SecurityCritical>]
        member p.IsPackageSourceEnabled(s) =
            s.IsOfficial || psp.IsPackageSourceEnabled(s)

        [<SecurityCritical>]
        member p.DisablePackageSource(s) =
            if not s.IsOfficial then
                psp.DisablePackageSource s

        [<SecurityCritical>]
        member p.LoadPackageSources() =
            let ss = psp.LoadPackageSources()
            if Seq.isEmpty ss then
                let s = PackageSource("https://nuget.org/api/v2/")
                s.IsEnabled <- true
                s.IsOfficial <- true
                Seq.singleton s
            else ss

        [<SecurityCritical>]
        member p.SavePackageSources(ss) =
            psp.SavePackageSources(ss)

[<AutoOpen>]
module NuGetConfigTools =

    let localRepositoryPath =
        Parameter.Create("packages")

    [<SecuritySafeCritical>]
    let makeSettings () =
        let dir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
        let nuGetDir = Path.Combine(dir, "NuGet")
        let fs = PhysicalFileSystem(nuGetDir)
        if fs.FileExists("NuGet.Config") then
            Settings(fs, "NuGet.Config") :> ISettings
            |> Some
        else
            None
        |> box

    [<SecuritySafeCritical>]
    let unboxParameter (p: Parameter<obj>) : Parameter<'T> =
        Parameter.Convert unbox box p

    let currentSettings =
        Parameter.Define (fun _ -> makeSettings ())

    [<SecuritySafeCritical>]
    let convert (f: 'A -> obj) : Func<'A,'B> =
        Func<_,_>(fun x -> unbox (f x))

    [<SecuritySafeCritical>]
    let makeHttpBuilder (log: Log) (psp: obj) (cp: obj) : Uri -> obj =
        fun (uri: Uri) ->
            box (NuGetConfigHttpClient(log, uri, unbox psp, unbox cp) :> IHttpClient)

    [<SecuritySafeCritical>]
    let makeRepository env =
        let cs = currentSettings.Find env :?> option<NuGet.ISettings>
        match cs with
        | Some settings ->
            let psp = NuGetPackageSourceProviderWrapper(PackageSourceProvider(settings))
            let cp = SettingsCredentialProvider(NullCredentialProvider.Instance, psp)
            let pspBox = box psp
            let cpBox = box cp
            let factory = PackageRepositoryFactory()
            let log = Log.Create<NuGetConfigHttpClient>(env)
            factory.HttpClientFactory <- convert (makeHttpBuilder log pspBox cpBox)
            let repo = psp.GetAggregate(factory)
            repo :> IPackageRepository
        | None ->
            let defaultSource = "https://nuget.org/api/v2/"
            PackageRepositoryFactory.Default.CreateRepository(defaultSource)

    [<SecuritySafeCritical>]
    let makePackageManager env =
        let path = localRepositoryPath.Find env
        let repo = makeRepository env
        let pm = PackageManager(repo, path) :> IPackageManager
        pm.Logger <- NuGetLogger env
        box pm

    let manager =
        Parameter.Define makePackageManager

    let packageOutputPath =
        Parameter.Define(fun env ->
            match Environment.GetEnvironmentVariable("NuGetPackageOutputPath") with
            | null | "" -> BuildConfig.BuildDir.Find env
            | dir -> dir)

[<Sealed>]
type NuGetConfig() =

    static member CurrentSettings
        with [<SecuritySafeCritical>] get () : Parameter<option<NuGet.ISettings>> =
            unboxParameter currentSettings

    static member CurrentPackageManager
        with [<SecuritySafeCritical>] get () : Parameter<NuGet.IPackageManager> =
            unboxParameter manager

    static member LocalRepositoryPath =
        localRepositoryPath

    static member PackageOutputPath =
        packageOutputPath

[<Sealed>]
type NuGetFile =

    /// Reads a local file as a `INuGetFile` file.
    static member Local(sourcePath: string, targetPath: string) =
        {
            new INuGetFile with
                member p.Read() = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite) :> Stream
                member p.TargetPath = targetPath
        }

    /// Reads a library file as an `INuGetFile` in a `lib/netXX` folder.
    static member LibraryFile(framework: Framework, sourcePath: string) =
        NuGetFile.Local(sourcePath, "/lib/" + framework.Name + "/" + Path.GetFileName sourcePath)
