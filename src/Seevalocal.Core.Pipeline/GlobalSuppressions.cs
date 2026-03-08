// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Seevalocal.Core.Pipeline.EvalPipeline.RunItemAsync(Seevalocal.Core.EvalStageContext,System.Boolean,System.String)~System.Threading.Tasks.Task{Seevalocal.Core.EvalResult}")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Seevalocal.Core.Pipeline.InMemoryResultCollector.CollectAsync(Seevalocal.Core.EvalResult,System.Threading.CancellationToken)~System.Threading.Tasks.Task")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Seevalocal.Core.Pipeline.InMemoryResultCollector.FinalizeAsync(System.Threading.CancellationToken)~System.Threading.Tasks.Task")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Seevalocal.Core.Pipeline.PipelineOrchestrator.RunAsync(System.Int32,System.Threading.CancellationToken)~System.Threading.Tasks.Task")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Seevalocal.Core.Pipeline.Stages.ExactMatchStage.ExecuteAsync(Seevalocal.Core.EvalStageContext)~System.Threading.Tasks.Task{Seevalocal.Core.StageResult}")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Seevalocal.Core.Pipeline.Stages.ExternalProcessStage.ExecuteAsync(Seevalocal.Core.EvalStageContext)~System.Threading.Tasks.Task{Seevalocal.Core.StageResult}")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Seevalocal.Core.Pipeline.Stages.FileWriterStage.ExecuteAsync(Seevalocal.Core.EvalStageContext)~System.Threading.Tasks.Task{Seevalocal.Core.StageResult}")]
[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "<Pending>", Scope = "member", Target = "~M:Seevalocal.Core.Pipeline.Stages.PromptStage.ExecuteAsync(Seevalocal.Core.EvalStageContext)~System.Threading.Tasks.Task{Seevalocal.Core.StageResult}")]
