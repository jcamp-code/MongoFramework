using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using MongoFramework.Infrastructure.Mapping;

namespace MongoFramework.Infrastructure.Serialization
{
	public static class TypeDiscovery
	{
		private static ReaderWriterLockSlim TypeCacheLock { get; } = new ReaderWriterLockSlim();
		private static HashSet<Type> AssignableTypes { get; set; } = new HashSet<Type>();

		public static void ClearCache()
		{
			TypeCacheLock.EnterWriteLock();

			try
			{
				AssignableTypes.Clear();
			}
			finally
			{
				TypeCacheLock.ExitWriteLock();
			}
		}

		public static Type FindTypeByDiscriminator(string name, Type expectedAssignableType)
		{
			TypeCacheLock.EnterUpgradeableReadLock();

			try
			{
				foreach (var type in AssignableTypes)
				{
					if (type.Name == name && expectedAssignableType.IsAssignableFrom(type))
					{
						return type;
					}
				}

				TypeCacheLock.EnterWriteLock();
				try
				{
					foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
					{
						if (assembly.FullName.StartsWith("System.") || assembly.FullName.StartsWith("Microsoft."))
						{
							continue;
						}

						if (!assembly.IsDynamic)
						{
							foreach (var type in assembly.GetTypes())
							{
								if (type.Name == name && expectedAssignableType.IsAssignableFrom(type))
								{
									AssignableTypes.Add(type);
									return type;
								}
							}
						}
					}
				}
				finally
				{
					TypeCacheLock.ExitWriteLock();
				}

				return default;
			}
			finally
			{
				TypeCacheLock.ExitUpgradeableReadLock();
			}
		}
	}

	public abstract class TypeDiscoverySerializer
	{
		protected static Type[] DictionaryTypes { get; } = new[] { typeof(IDictionary<,>), typeof(Dictionary<,>) };
	}

	public class TypeDiscoverySerializer<TEntity> : TypeDiscoverySerializer, IBsonSerializer<TEntity>, IBsonDocumentSerializer, IBsonIdProvider where TEntity : class
	{

		public Type ValueType => typeof(TEntity);

		public object Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
		{
			var type = context.Reader.GetCurrentBsonType();
			if (type == BsonType.Document)
			{
				var bookmark = context.Reader.GetBookmark();
				context.Reader.ReadStartDocument();

				var actualType = ValueType;
				if (context.Reader.FindElement("_t"))
				{
					var discriminator = BsonValueSerializer.Instance.Deserialize(context);
					if (discriminator.IsBsonArray)
					{
						discriminator = discriminator.AsBsonArray.Last();
					}
					if (discriminator.IsString)
					{
						actualType = TypeDiscovery.FindTypeByDiscriminator(discriminator.AsString, args.NominalType);
					}
				}
				context.Reader.ReturnToBookmark(bookmark);

				var serializer = GetRealSerializer(actualType);
				var deserializedResult = serializer.Deserialize(context);
				return deserializedResult;
			}
			else if (type == BsonType.Null)
			{
				context.Reader.ReadNull();
				return default(TEntity);
			}
			else if (type == BsonType.Array)
			{
				return new ArraySerializer<object>().Deserialize(context);
			}
			else if (type == BsonType.Boolean)
			{
				return context.Reader.ReadBoolean();
			}
			else if (type == BsonType.DateTime)
			{
				return new BsonDateTime(context.Reader.ReadDateTime()).ToUniversalTime();
			}
			else if (type == BsonType.Double)
			{
				return context.Reader.ReadDouble();
			}
			else if (type == BsonType.Int32)
			{
				return context.Reader.ReadInt32();
			}
			else if (type == BsonType.Int64)
			{
				return context.Reader.ReadInt64();
			}
			else if (type == BsonType.ObjectId)
			{
				return context.Reader.ReadObjectId();
			}
			else if (type == BsonType.String)
			{
				return context.Reader.ReadString();
			}
			else
			{
				throw new NotSupportedException($"Unsupported BsonType {type} for TypeDiscoverySerializer");
			}
		}

		private static bool IsCollectionType(Type type)
		{
			return type != null
				&& type != typeof(string)
				&& !typeof(BsonValue).IsAssignableFrom(type)
				&& typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
				&& !(type.IsGenericType && DictionaryTypes.Contains(type.GetGenericTypeDefinition()));
		}

		private IBsonSerializer GetRealSerializer(Type type)
		{
			if (type == null || type == typeof(object))
			{
				return new DictionaryInterfaceImplementerSerializer<Dictionary<string, object>>();
			}

			if (type.IsGenericType && DictionaryTypes.Contains(type.GetGenericTypeDefinition()))
			{
				var serializerType = typeof(DictionaryInterfaceImplementerSerializer<>).MakeGenericType(type);
				var serializer = (IBsonSerializer)Activator.CreateInstance(serializerType);
				return serializer;
			}

			// Collection types (List<T>, T[], HashSet<T>, etc.) are handled inline by the
			// Serialize method to avoid EntityMapping.TryRegisterType corrupting global state.
			// We return null here as a signal; the Serialize method checks IsCollectionType first.
			if (IsCollectionType(type))
			{
				return null;
			}

			if (EntityMapping.IsValidTypeToMap(type))
			{
				//Force the type to be processed by the Entity Mapper
				EntityMapping.TryRegisterType(type, out _);
				var classMap = BsonClassMap.LookupClassMap(type);
				var serializerType = typeof(BsonClassMapSerializer<>).MakeGenericType(type);
				var serializer = (IBsonSerializer)Activator.CreateInstance(serializerType, classMap);
				return serializer;
			}

			return BsonSerializer.LookupSerializer(type);
		}

		public void Serialize(BsonSerializationContext context, BsonSerializationArgs args, object value)
		{
			Serialize(context, args, (TEntity)value);
		}

		public void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TEntity value)
		{
			// Handle collection types directly by writing as BsonArray.
			// This avoids EntityMapping.TryRegisterType being called for types like List<string>,
			// which would create a broken BsonClassMap and corrupt the global serializer state.
			if (value is System.Collections.IEnumerable enumerable && IsCollectionType(value.GetType()))
			{
				var writer = context.Writer;
				writer.WriteStartArray();
				foreach (var item in enumerable)
				{
					BsonSerializer.Serialize(writer, typeof(object), item);
				}
				writer.WriteEndArray();
				return;
			}

			var serializer = GetRealSerializer(value?.GetType() ?? ValueType);
			serializer.Serialize(context, args, value);
		}

		public bool TryGetMemberSerializationInfo(string memberName, out BsonSerializationInfo serializationInfo)
		{
			var classMap = BsonClassMap.LookupClassMap(ValueType);
			return new BsonClassMapSerializer<TEntity>(classMap).TryGetMemberSerializationInfo(memberName, out serializationInfo);
		}

		TEntity IBsonSerializer<TEntity>.Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
		{
			return (TEntity)Deserialize(context, args);
		}

		public bool GetDocumentId(object document, out object id, out Type idNominalType, out IIdGenerator idGenerator)
		{
			var classMap = BsonClassMap.LookupClassMap(ValueType);
			return new BsonClassMapSerializer<TEntity>(classMap).GetDocumentId(document, out id, out idNominalType, out idGenerator);
		}

		public void SetDocumentId(object document, object id)
		{
			var classMap = BsonClassMap.LookupClassMap(ValueType);
			new BsonClassMapSerializer<TEntity>(classMap).SetDocumentId(document, id);
		}
	}
}
