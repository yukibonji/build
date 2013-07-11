﻿namespace IntelliFactory.Build

open System
open System.IO
open System.Runtime.Versioning
open NuGet

[<Sealed>]
type internal SafeNuGetSemanticVersion =
    new : version: Version * ?suffix: string -> SafeNuGetSemanticVersion

    member SpecialVersion : option<string>
    member Version : Version

    static member ForPackage : IPackage -> SafeNuGetSemanticVersion
    static member Parse : string -> SafeNuGetSemanticVersion

[<Sealed>]
type internal SafeNuGetPackageDependency =
    new : packageId: string * ?ver: SafeNuGetSemanticVersion -> SafeNuGetPackageDependency

[<Sealed>]
type internal SafeNuGetPackageAssemblyReference =
    member Name : string
    member Path : string
    member SupportedFrameworks : seq<FrameworkName>
    member TargetFramework : option<FrameworkName>

[<Sealed>]
type internal SafeNuGetFrameworkAssemblyReference =
    member AssemblyName : string
    member SupportedFrameworks : seq<FrameworkName>

[<Sealed>]
type internal SafeNuGetPackageFile =
    member EffectivePath : string
    member Path : string
    member SupportedFrameworks : seq<FrameworkName>
    member TargetFramework : option<FrameworkName>
    static member Create : sourcePath: string * targetPath: string -> SafeNuGetPackageFile

[<Sealed>]
type internal SafeNuGetPackage =
    member Compare : SafeNuGetPackage -> int
    member GetCompatiblePackageDependencies : FrameworkName -> seq<SafeNuGetPackageDependency>
    member GetToolFiles : unit -> seq<SafeNuGetPackageFile>
    member AssemblyReferences : seq<SafeNuGetPackageAssemblyReference>
    member FrameworkAssemblies : seq<SafeNuGetFrameworkAssemblyReference>
    member Id : string
    member Version : SafeNuGetSemanticVersion

[<Sealed>]
type internal SafePackageRepository =
    member FindByDependency : dep: SafeNuGetPackageDependency * ?allowPreRelease: bool * ?allowUnlisted: bool -> option<SafeNuGetPackage>
    member FindById : id: string -> option<SafeNuGetPackage>
    member FindExact : id: string * ver: SafeNuGetSemanticVersion * ?allowPreRelease: bool * ?allowUnlisted: bool -> option<SafeNuGetPackage>

[<Sealed>]
type internal SafeNuGetPackageManager =
    new : IPackageManager -> SafeNuGetPackageManager
    member GetPackageDirectory : SafeNuGetPackage -> option<string>
    member Install : pkg: SafeNuGetPackage * ?ignoreDependencies: bool * ?allowPreRelease: bool -> unit
    member InstallExact : pid: string * ver: SafeNuGetSemanticVersion * ?ignoreDependencies: bool * ?allowPreRelease: bool -> unit
    member LocalRepository : SafePackageRepository
    member SourceRepository : SafePackageRepository

[<Sealed>]
type SafeNuGetPackageDependencySet =
    member Dependencies : seq<SafeNuGetPackageDependency>
    member SupportedFrameworks : seq<FrameworkName>
    member TargetFramework : FrameworkName
    static member Create : seq<SafeNuGetPackageDependency> * ?framework: FrameworkName -> SafeNuGetPackageDependencySet

[<Sealed>]
type internal SafeNuGetPackageBuilder =
    new : unit -> SafeNuGetPackageBuilder
    new : Stream * string -> SafeNuGetPackageBuilder
    member Save : Stream -> unit
    member Authors : seq<string> with get, set
    member DependencySets : seq<SafeNuGetPackageDependencySet> with get, set
    member Description : string with get, set
    member Files : seq<SafeNuGetPackageFile> with get, set
    member Id : string with get, set
    member LicenseUrl : Uri with get, set
    member ProjectUrl : Uri with get, set
    member RequireLicenseAcceptance : bool with get, set
    member Version : SafeNuGetSemanticVersion with get, set