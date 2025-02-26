// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace System.Text.Json
{
    [StructLayout(LayoutKind.Auto)]
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    internal struct ReadStackFrame
    {
        // Current property values.
        public JsonPropertyInfo? JsonPropertyInfo;
        public StackFramePropertyState PropertyState;
        public bool UseExtensionProperty;

        // Support JSON Path on exceptions and non-string Dictionary keys.
        // This is Utf8 since we don't want to convert to string until an exception is thown.
        // For dictionary keys we don't want to convert to TKey until we have both key and value when parsing the dictionary elements on stream cases.
        public byte[]? JsonPropertyName;
        public string? JsonPropertyNameAsString; // This is used for string dictionary keys and re-entry cases that specify a property name.

        // Stores the non-string dictionary keys for continuation.
        public object? DictionaryKey;

#if DEBUG
        // Validation state.
        public int OriginalDepth;
        public JsonTokenType OriginalTokenType;
#endif

        // Current object (POCO or IEnumerable).
        public object? ReturnValue; // The current return value used for re-entry.
        public JsonTypeInfo JsonTypeInfo;
        public StackFrameObjectState ObjectState; // State tracking the current object.

        // Current object can contain metadata
        public bool CanContainMetadata;
        public MetadataPropertyName LatestMetadataPropertyName;
        public MetadataPropertyName MetadataPropertyNames;

        // Serialization state for value serialized by the current frame.
        public PolymorphicSerializationState PolymorphicSerializationState;

        // Holds any entered polymorphic JsonTypeInfo metadata.
        public JsonTypeInfo? PolymorphicJsonTypeInfo;

        // Gets the initial JsonTypeInfo metadata used when deserializing the current value.
        public JsonTypeInfo BaseJsonTypeInfo
            => PolymorphicSerializationState == PolymorphicSerializationState.PolymorphicReEntryStarted
                ? PolymorphicJsonTypeInfo!
                : JsonTypeInfo;

        // For performance, we order the properties by the first deserialize and PropertyIndex helps find the right slot quicker.
        public int PropertyIndex;
        public List<PropertyRef>? PropertyRefCache;

        // Holds relevant state when deserializing objects with parameterized constructors.
        public int CtorArgumentStateIndex;
        public ArgumentState? CtorArgumentState;

        // Whether to use custom number handling.
        public JsonNumberHandling? NumberHandling;

        public void EndConstructorParameter()
        {
            CtorArgumentState!.JsonParameterInfo = null;
            JsonPropertyName = null;
            PropertyState = StackFramePropertyState.None;
        }

        public void EndProperty()
        {
            JsonPropertyInfo = null!;
            JsonPropertyName = null;
            JsonPropertyNameAsString = null;
            PropertyState = StackFramePropertyState.None;

            // No need to clear these since they are overwritten each time:
            //  NumberHandling
            //  UseExtensionProperty
        }

        public void EndElement()
        {
            JsonPropertyNameAsString = null;
            PropertyState = StackFramePropertyState.None;
        }

        /// <summary>
        /// Is the current object a Dictionary.
        /// </summary>
        public bool IsProcessingDictionary()
        {
            return (JsonTypeInfo.PropertyInfoForTypeInfo.ConverterStrategy & ConverterStrategy.Dictionary) != 0;
        }

        /// <summary>
        /// Is the current object an Enumerable.
        /// </summary>
        public bool IsProcessingEnumerable()
        {
            return (JsonTypeInfo.PropertyInfoForTypeInfo.ConverterStrategy & ConverterStrategy.Enumerable) != 0;
        }

        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private string DebuggerDisplay => $"ConverterStrategy.{JsonTypeInfo?.PropertyInfoForTypeInfo.ConverterStrategy}, {JsonTypeInfo?.Type.Name}";
    }
}
