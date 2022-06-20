﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.Quantum.QsCompiler.Experimental

open System
open System.Collections.Immutable
open Microsoft.Quantum.QsCompiler
open Microsoft.Quantum.QsCompiler.Experimental
open Microsoft.Quantum.QsCompiler.Experimental.OptimizationTools
open Microsoft.Quantum.QsCompiler.SyntaxExtensions
open Microsoft.Quantum.QsCompiler.SyntaxTree


type PreEvaluation =

    /// Attempts to pre-evaluate the given sequence of namespaces
    /// as much as possible with a script of optimizing transformations
    ///
    /// Some of the optimizing transformations need a dictionary of all
    /// callables by name.  Consequently, the script is generated by a
    /// function that takes as input such a dictionary of callables.
    ///
    /// Disclaimer: This is an experimental feature.
    static member WithScript
        (script: Func<ImmutableDictionary<QsQualifiedName, QsCallable>, TransformationBase seq>)
        (arg: QsCompilation)
        =

        // TODO: this should actually only evaluate everything for each entry point
        let rec evaluate (tree: _ list) =
            let mutable tree = tree
            tree <- List.map (StripAllKnownSymbols().Namespaces.OnNamespace) tree
            tree <- List.map (VariableRenaming().Namespaces.OnNamespace) tree

            let callables = GlobalCallableResolutions tree // needs to be constructed in every iteration
            let optimizers = script.Invoke callables |> Seq.toList

            for opt in optimizers do
                tree <- List.map opt.Namespaces.OnNamespace tree

            if optimizers |> List.exists (fun opt -> opt.CheckChanged()) then evaluate tree else tree

        let namespaces = arg.Namespaces |> Seq.map StripPositionInfo.Apply |> List.ofSeq |> evaluate
        QsCompilation.New(namespaces.ToImmutableArray(), arg.EntryPoints)

    /// Default sequence of optimizing transformations
    static member DefaultScript removeFunctions maxSize : Func<_, TransformationBase seq> =
        new Func<_, _>(fun callables ->
            seq {
                VariableRemoval()
                StatementRemoval(removeFunctions)
                ConstantPropagation(callables)
                LoopUnrolling(callables, maxSize)
                CallableInlining(callables)
                StatementGrouping()
                PureCircuitFinder(callables)
            })

    /// Attempts to pre-evaluate the given sequence of namespaces
    /// as much as possible with a default optimization script
    static member All(arg: QsCompilation) =
        PreEvaluation.WithScript (PreEvaluation.DefaultScript false 40) arg
