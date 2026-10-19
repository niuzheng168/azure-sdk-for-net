// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using NUnit.Framework;

#pragma warning disable AAIP001 // These tests exercise preview voice conversation models.

namespace Azure.AI.Projects.Agents.Tests;

public class VoiceConversationErrorTests
{
    [TestCase(typeof(AgentEndpointLiveConversations))]
    [TestCase(typeof(AgentEndpointLiveConversationsGetLiveAgentConversationsCollectionResult))]
    [TestCase(typeof(AgentEndpointLiveConversationsGetLiveAgentConversationsAsyncCollectionResult))]
    [TestCase(typeof(AgentEndpointLiveConversationsGetLiveAgentConversationItemsCollectionResult))]
    [TestCase(typeof(AgentEndpointLiveConversationsGetLiveAgentConversationItemsAsyncCollectionResult))]
    public void LiveConversationClientsArePreview(Type clientType)
    {
        ExperimentalAttribute attribute = clientType.GetCustomAttribute<ExperimentalAttribute>();
        Assert.That(attribute, Is.Not.Null);
        Assert.That(attribute.DiagnosticId, Is.EqualTo("AAIP001"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeDelegationKeepsWireShapeAfterSdkRename(bool responses)
    {
        BinaryData data = BinaryData.FromString(responses
            ? """{"type":"responses","responses":{"model":"teacher-backend"}}"""
            : """{"type":"client"}""");

        LiveDelegation delegation = ModelReaderWriter.Read<LiveDelegation>(data);
        Assert.That(delegation.GetType(), Is.EqualTo(responses
            ? typeof(LiveResponsesDelegationParam)
            : typeof(LiveClientDelegationParam)));

        using JsonDocument document = JsonDocument.Parse(ModelReaderWriter.Write(delegation).ToString());
        Assert.That(document.RootElement.GetProperty("type").GetString(), Is.EqualTo(responses ? "responses" : "client"));
        if (responses)
        {
            Assert.That(document.RootElement.GetProperty("responses").GetProperty("model").GetString(),
                Is.EqualTo("teacher-backend"));
        }
    }

    [TestCase(typeof(VoiceConversation))]
    [TestCase(typeof(VoiceAgentLiveConversation))]
    public void LastErrorIsInternal(Type conversationType)
    {
        Assert.That(conversationType.GetProperty("LastError", BindingFlags.Instance | BindingFlags.Public), Is.Null);
        PropertyInfo property = conversationType.GetProperty("LastError", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(property, Is.Not.Null);
        Assert.That(property.GetMethod.IsAssembly, Is.True);
        Assert.That(property.PropertyType, Is.EqualTo(typeof(FoundryOpenAIError)));
        Assert.That(property.PropertyType.IsVisible, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedConversationRetainsLastError(bool live)
    {
        BinaryData data = BinaryData.FromString("""
            {
              "id": "conv_failed",
              "object": "voice.conversation",
              "status": "failed",
              "created_at": 1792108800,
              "last_error": {
                "code": "persistence_failed",
                "message": "Unable to finalize the recording.",
                "param": "audio",
                "type": "server_error",
                "details": [
                  {
                    "code": "storage_unavailable",
                    "message": "Audio storage is unavailable."
                  }
                ],
                "additionalInfo": { "retryable": true },
                "debugInfo": { "request_id": "req_test" }
              }
            }
            """);

        BinaryData serialized = RoundTrip(data, live, out FoundryOpenAIError error);

        Assert.That(error, Is.Not.Null);
        Assert.That(error.Code, Is.EqualTo("persistence_failed"));
        Assert.That(error.Message, Is.EqualTo("Unable to finalize the recording."));
        Assert.That(error.Param, Is.EqualTo("audio"));
        Assert.That(error.Type, Is.EqualTo("server_error"));
        Assert.That(error.Details, Has.Count.EqualTo(1));
        Assert.That(error.Details[0].Code, Is.EqualTo("storage_unavailable"));
        using JsonDocument document = JsonDocument.Parse(serialized.ToString());
        JsonElement lastError = document.RootElement.GetProperty("last_error");
        Assert.That(lastError.GetProperty("code").GetString(), Is.EqualTo(error.Code));
        Assert.That(lastError.GetProperty("message").GetString(), Is.EqualTo(error.Message));
        Assert.That(lastError.GetProperty("param").GetString(), Is.EqualTo(error.Param));
        Assert.That(lastError.GetProperty("type").GetString(), Is.EqualTo(error.Type));
        Assert.That(lastError.GetProperty("details")[0].GetProperty("code").GetString(), Is.EqualTo(error.Details[0].Code));
        Assert.That(lastError.GetProperty("details")[0].GetProperty("message").GetString(), Is.EqualTo(error.Details[0].Message));
        Assert.That(lastError.GetProperty("additionalInfo").GetProperty("retryable").GetBoolean(), Is.True);
        Assert.That(lastError.GetProperty("debugInfo").GetProperty("request_id").GetString(), Is.EqualTo("req_test"));
    }

    [TestCase(false, "in_progress")]
    [TestCase(true, "in_progress")]
    [TestCase(false, "completed")]
    [TestCase(true, "completed")]
    [TestCase(false, "failed")]
    [TestCase(true, "failed")]
    public void MissingLastErrorStaysAbsent(bool live, string status)
    {
        BinaryData data = BinaryData.FromString($$"""
            {
              "id": "conv_no_error",
              "object": "voice.conversation",
              "status": "{{status}}",
              "created_at": 1792108800
            }
            """);

        BinaryData serialized = RoundTrip(data, live, out FoundryOpenAIError error);

        Assert.That(error, Is.Null);
        using JsonDocument document = JsonDocument.Parse(serialized.ToString());
        Assert.That(document.RootElement.GetProperty("status").GetString(), Is.EqualTo(status));
        Assert.That(document.RootElement.TryGetProperty("last_error", out _), Is.False);
    }

    private static BinaryData RoundTrip(BinaryData data, bool live, out FoundryOpenAIError error)
    {
        if (live)
        {
            VoiceAgentLiveConversation conversation = ModelReaderWriter.Read<VoiceAgentLiveConversation>(data);
            error = conversation.LastError;
            return ModelReaderWriter.Write(conversation);
        }

        VoiceConversation realtime = ModelReaderWriter.Read<VoiceConversation>(data);
        error = realtime.LastError;
        return ModelReaderWriter.Write(realtime);
    }
}
