// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Extensibility
{
    /// <summary>
    /// Base class for providers of <see cref="ScriptBinding"/>s.
    /// </summary>
    public abstract class ScriptBindingProvider
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        /// <param name="config">The <see cref="JobHostConfiguration"/>.</param>
        public ScriptBindingProvider(JobHostConfiguration config)
        {
            Config = config;
        }

        /// <summary>
        /// Gets the <see cref="JobHostConfiguration"/>.
        /// </summary>
        protected JobHostConfiguration Config { get; private set; }

        /// <summary>
        /// Called after all bindings have been created to allow the provider
        /// to perform final host level initialization.
        /// </summary>
        /// <param name="traceWriter">The <see cref="TraceWriter"/> to log to as needed.</param>
        /// <param name="hostMetadata">The host configuration metadata.</param>
        public virtual void Initialize(TraceWriter traceWriter, JObject hostMetadata)
        {
        }

        /// <summary>
        /// Create a <see cref="ScriptBinding"/> for the specified metadata if
        /// </summary>
        /// <param name="bindingMetadata">The metadata for the binding.</param>
        /// <param name="binding">The corresponding <see cref="ScriptBinding"/>.</param>
        /// <returns>True if a binding was created, false otherwise.</returns>
        public abstract bool TryCreate(JObject bindingMetadata, out ScriptBinding binding);
    }
}
