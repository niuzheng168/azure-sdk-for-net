// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Azure.AI.Projects.Agents;

public partial class VoiceAgentLiveConversation
{
    /// <summary> The terminal persistence error, present when finalization fails. </summary>
    internal FoundryOpenAIError LastError { get; }
}
