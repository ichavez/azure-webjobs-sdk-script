// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings.Path;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Binding
{
    // TEMP: Wrapper used to adapt a ScriptBinding to the existing FunctionBinding infrastructure 
    [CLSCompliant(false)]
    public class ExtensionBinding : FunctionBinding
    {
        private ScriptBinding _binding;
        private IDictionary<Type, IDictionary<string, object>> _attributeData;

        public ExtensionBinding(ScriptHostConfiguration config, ScriptBinding binding, BindingMetadata metadata) : base(config, metadata, binding.Access)
        {
            _binding = binding;

            // TEMP: We're doing some conversions from Attribute instances to data bags
            // which will go away
            var attributes = _binding.GetAttributes();
            _attributeData = new Dictionary<Type, IDictionary<string, object>>();
            foreach (var attribute in attributes)
            {
                _attributeData.Add(attribute.GetType(), GetAttributeData(attribute));
            }
        }

        public override Collection<CustomAttributeBuilder> GetCustomAttributes(Type parameterType)
        {
            Collection<CustomAttributeBuilder> attributeBuilders = new Collection<CustomAttributeBuilder>();
            foreach (var attribute in _attributeData)
            {
                CustomAttributeBuilder builder = GetAttributeBuilder(attribute.Key, attribute.Value);
                attributeBuilders.Add(builder);
            }

            return attributeBuilders;
        }

        public override async Task BindAsync(BindingContext context)
        {
            // All the below BindAsync logic is temporary IBinder support
            // Once the Invoker work is done, we'll be binding directly
            Collection<Attribute> attributes = CreateAttributes(_attributeData, context.BindingData);
            var attribute = attributes.First();
            var additionalAttributes = attributes.Skip(1).ToArray();
            RuntimeBindingContext runtimeContext = new RuntimeBindingContext(attribute, additionalAttributes);

            // TEMP: We'll be doing away with this IBinder code
            // So for now we don't support binding parameters for Non C#
            if (_binding.DefaultType == typeof(IAsyncCollector<byte[]>))
            {
                await BindAsyncCollectorAsync<byte[]>(context, runtimeContext);
            }
            else if (_binding.DefaultType == typeof(Stream))
            {
                await BindStreamAsync(context, Access, runtimeContext);
            }
            else if (_binding.DefaultType == typeof(JObject))
            {
                var result = await context.Binder.BindAsync<JObject>(runtimeContext);
                if (Access == FileAccess.Read)
                {
                    context.Value = result;
                }
            }
            else if (_binding.DefaultType == typeof(IAsyncCollector<JObject>))
            {
                await BindAsyncCollectorAsync<JObject>(context, runtimeContext);
            }
        }

        // TEMP - Since we're still using IBinder for non C#, we have to construct the Attributes
        private Collection<Attribute> CreateAttributes(IDictionary<Type, IDictionary<string, object>> attributesToCreate, IReadOnlyDictionary<string, string> bindingData)
        {
            Collection<Attribute> attributes = new Collection<Attribute>();

            foreach (var attributeToCreate in attributesToCreate)
            {
                // access and resolve all attribute data
                var attributeData = attributeToCreate.Value;
                if (bindingData != null)
                {
                    foreach (var pair in attributeData.Where(p => p.Value is string).ToArray())
                    {
                        string resolvedValue = ResolveAndBind((string)pair.Value, bindingData);
                        attributeData[pair.Key] = resolvedValue;
                    }
                }

                // construct the attribute instance
                var attributeConstructionInfo = GetAttributeConstructionInfo(attributeToCreate.Key, attributeData);
                Attribute attribute = (Attribute)attributeConstructionInfo.Constructor.Invoke(attributeConstructionInfo.ConstructorArgs);

                // apply any named property values
                foreach (var namedProperty in attributeConstructionInfo.Properties)
                {
                    namedProperty.Key.SetValue(attribute, namedProperty.Value);
                }

                attributes.Add(attribute);
            }

            return attributes;
        }

        // TEMP
        private string ResolveAndBind(string value, IReadOnlyDictionary<string, string> bindingData)
        {
            BindingTemplate template = BindingTemplate.FromString(value);

            string boundValue = value;

            if (bindingData != null)
            {
                if (template != null)
                {
                    boundValue = template.Bind(bindingData);
                }
            }

            if (!string.IsNullOrEmpty(value))
            {
                boundValue = Resolve(boundValue);
            }

            return boundValue;
        }

        internal class AttributeConstructionInfo
        {
            public ConstructorInfo Constructor { get; set; }
            public object[] ConstructorArgs { get; set; }
            public IDictionary<PropertyInfo, object> Properties { get; set; }
        }

        internal static CustomAttributeBuilder GetAttributeBuilder(Type attributeType, IDictionary<string, object> attributeData)
        {
            AttributeConstructionInfo constructionInfo = GetAttributeConstructionInfo(attributeType, attributeData);

            var namedProperties = constructionInfo.Properties.Keys.ToArray();
            var namedPropertyValues = constructionInfo.Properties.Values.ToArray();
            CustomAttributeBuilder builder = new CustomAttributeBuilder(constructionInfo.Constructor, constructionInfo.ConstructorArgs, namedProperties, namedPropertyValues);

            return builder;
        }

        internal static IDictionary<string, object> GetAttributeData(Attribute attribute)
        {
            Dictionary<string, object> data = new Dictionary<string, object>();

            foreach (var property in attribute.GetType().GetProperties())
            {
                object value = property.GetValue(attribute);
                if (value != null)
                {
                    data.Add(property.Name, value);
                }
            }

            return data;
        }

        internal static AttributeConstructionInfo GetAttributeConstructionInfo(Type attributeType, IDictionary<string, object> attributeData)
        {
            Dictionary<string, object> attributeDataCaseInsensitive = new Dictionary<string, object>(attributeData, StringComparer.OrdinalIgnoreCase);

            // Pick the ctor with the longest parameter list where all parameters are matched.
            int longestMatch = -1;
            ConstructorInfo bestCtor = null;
            Dictionary<PropertyInfo, object> propertiesToSet = null;
            object[] constructorArgs = null;
            var ctors = attributeType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            foreach (var currCtor in ctors)
            {
                var ps = currCtor.GetParameters();
                int len = ps.Length;

                object[] currConstructorArgs = new object[len];

                bool hasAllParameters = true;
                for (int i = 0; i < len; i++)
                {
                    var p = ps[i];
                    object value = null;
                    if (!attributeDataCaseInsensitive.TryGetValue(p.Name, out value) || value == null)
                    {
                        hasAllParameters = false;
                        break;
                    }

                    currConstructorArgs[i] = value;
                }

                if (hasAllParameters)
                {
                    if (len > longestMatch)
                    {
                        propertiesToSet = new Dictionary<PropertyInfo, object>();

                        // Set any remaining property values
                        foreach (var prop in attributeType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                        {
                            if (!prop.CanWrite || !prop.GetSetMethod(/*nonPublic*/ true).IsPublic ||
                                Nullable.GetUnderlyingType(prop.PropertyType) != null)
                            {
                                continue;
                            }

                            object objValue = null;
                            if (attributeDataCaseInsensitive.TryGetValue(prop.Name, out objValue))
                            {
                                propertiesToSet.Add(prop, objValue);
                            }
                        }

                        bestCtor = currCtor;
                        constructorArgs = currConstructorArgs;
                        longestMatch = len;
                    }
                }
            }

            if (bestCtor == null)
            {
                // error!!!
                throw new InvalidOperationException("Can't figure out which ctor to call.");
            }

            AttributeConstructionInfo info = new AttributeConstructionInfo
            {
                Constructor = bestCtor,
                ConstructorArgs = constructorArgs,
                Properties = propertiesToSet
            };

            return info;
        }
    }
}
