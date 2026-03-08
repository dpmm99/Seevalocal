// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.
using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging", Justification = "Just don't want to see this when there are more significant code suggestions to look at.", Scope = "module")]
