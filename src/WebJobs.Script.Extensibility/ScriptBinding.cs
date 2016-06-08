// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.ObjectModel;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensibility
{
    /// <summary>
    /// Represents a 
    /// </summary>
    public abstract class ScriptBinding
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="bindingMetadata">The binding metadata.</param>
        public ScriptBinding(JObject bindingMetadata)
        {
            Metadata = bindingMetadata;

            // TODO: read from metadata
            string direction = (string)bindingMetadata.GetValue("direction", StringComparison.OrdinalIgnoreCase) ?? "in";
            switch (direction.ToLowerInvariant())
            {
                case "in":
                    Access = FileAccess.Read;
                    break;
                case "out":
                    Access = FileAccess.Write;
                    break;
                case "inout":
                    Access = FileAccess.ReadWrite;
                    break;
            }

            IsTrigger = ((string)bindingMetadata.GetValue("type", StringComparison.OrdinalIgnoreCase)).EndsWith("trigger", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the binding metadata.
        /// </summary>
        public JObject Metadata { get; private set; }

        /// <summary>
        /// Gets the <see cref="FileAccess"/> for this binding.
        /// </summary>
        public FileAccess Access { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this binding is a trigger binding.
        /// </summary>
        public bool IsTrigger { get; private set; }

        /// <summary>
        /// Gets the default <see cref="Type"/> that this binding should
        /// use to bind.
        /// </summary>
        public abstract Type DefaultType { get; }

        /// <summary>
        /// Gets the collection of <see cref="Attribute"/>s that should
        /// be applied to the binding.
        /// </summary>
        /// <returns></returns>
        public abstract Collection<Attribute> GetAttributes();

        protected TEnum GetEnumValue<TEnum>(string key, TEnum defaultValue = default(TEnum)) where TEnum : struct
        {
            string rawValue = GetValue<string>(key);

            TEnum enumValue = default(TEnum);
            if (!string.IsNullOrEmpty(rawValue) &&
                Enum.TryParse<TEnum>(rawValue, true, out enumValue))
            {
                return enumValue;
            }

            return defaultValue;
        }

        protected TValue GetValue<TValue>(string key, TValue defaultValue = default(TValue))
        {
            JToken value = null;
            if (Metadata.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out value))
            {
                return value.Value<TValue>();
            }

            return defaultValue;
        }
    }
}
