using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VSCodeDebugging.VSCodeProtocol
{
	public class ConverterContext
	{
		public ConverterContext(IDictionary<object, object> data, ConverterContext parentContext)
		{
			Data = data;
			ParentContext = parentContext;
		}

		public IDictionary<object, object> Data { get; }

		public ConverterContext ParentContext { get; }
	}

	
	public abstract class JsonCreationConverter<T> : JsonConverter
	{
		/// <summary>
		/// Create an instance of objectType, based properties in the JSON object
		/// </summary>
		/// <param name="objectType">type of object expected</param>
		/// <param name="jObject">
		///     contents of JSON object that will be deserialized
		/// </param>
		/// <param name="serializer"></param>
		/// <param name="context"></param>
		/// <returns></returns>
		protected abstract T Create(Type objectType, JObject jObject, JsonSerializer serializer, ConverterContext context);

		/// <summary>
		/// Set to true if you're creating objects via new and doesn't fill it's properties in Create(). Set to false if your create your object via Deserialize()
		/// </summary>
		protected abstract bool PopulateAfterCreation { get; }

		public override bool CanConvert(Type objectType)
		{
			return typeof(T).IsAssignableFrom(objectType);
		}

		public override object ReadJson(JsonReader reader,
			Type objectType,
			object existingValue,
			JsonSerializer serializer)
		{
			// Load JObject from stream
			if (reader.TokenType == JsonToken.Null)
				return null;
			var jObject = JObject.Load(reader);

			var originalContext = serializer.Context;

			var converterContext = new ConverterContext(new Dictionary<object, object>(), originalContext.Context as ConverterContext);
			serializer.Context = new StreamingContext(serializer.Context.State, converterContext);
			// Create target object based on JObject
			var target = Create(objectType, jObject, serializer, converterContext);

			if (PopulateAfterCreation)
			{
				// Populate the object properties
				serializer.Populate(jObject.CreateReader(), target);
			}
			serializer.Context = originalContext;
			return target;
		}

		public override void WriteJson(JsonWriter writer,
			object value,
			JsonSerializer serializer)
		{
			throw new NotImplementedException();
		}
	}
}
