﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.AspNetCore.Blazor;

namespace BlazorDB.Storage
{
    internal class StorageManagerSave
    {
        public int SaveContextToLocalStorage(StorageContext context)
        {
            var total = 0;
            var contextType = context.GetType();
            Logger.ContextSaved(contextType);
            var storageSets = StorageManagerUtil.GetStorageSets(contextType);
            total = SaveStorageSets(context, total, contextType, storageSets);
            Logger.EndGroup();
            return total;
        }

        private static int SaveStorageSets(StorageContext context, int total, Type contextType,
            List<PropertyInfo> storageSets)
        {
            foreach (var prop in storageSets)
            {
                var storageSetValue = prop.GetValue(context);
                var modelType = prop.PropertyType.GetGenericArguments()[0];
                var storageTableName = Util.GetStorageTableName(contextType, modelType);
                var guids = SaveModels(storageSetValue, modelType, storageTableName, storageSets);
                total += guids.Count;
                var oldMetadata = StorageManagerUtil.LoadMetadata(storageTableName);
                SaveMetadata(storageTableName, guids, contextType, modelType);
                if (oldMetadata != null) DeleteOldModelsFromStorage(oldMetadata, storageTableName);
                Logger.StorageSetSaved(modelType, guids.Count);
            }

            return total;
        }

        private static void SaveMetadata(string storageTableName, List<Guid> guids, Type context, Type modelType)
        {
            var metadata = new Metadata
            {
                Guids = guids,
                ContextName = Util.GetFullyQualifiedTypeName(context),
                ModelName = Util.GetFullyQualifiedTypeName(modelType)
            };
            var name = $"{storageTableName}-{StorageManagerUtil.Metadata}";
            BlazorDBInterop.SetItem(name, JsonUtil.Serialize(metadata), false);
        }

        private static List<Guid> SaveModels(object storageSetValue, Type modelType, string storageTableName,
            List<PropertyInfo> storageSets)
        {
            var guids = new List<Guid>();
            var storageSetType = StorageManagerUtil.GenericStorageSetType.MakeGenericType(modelType);
            var method = storageSetType.GetMethod(StorageManagerUtil.GetEnumerator);
            var enumerator = (IEnumerator) method.Invoke(storageSetValue, new object[] { });
            var maxId = GetMaxId(enumerator);
            while (enumerator.MoveNext())
            {
                var guid = Guid.NewGuid();
                guids.Add(guid);
                var model = enumerator.Current;
                var name = $"{storageTableName}-{guid}";
                if (GetId(model) == 0) SetId(model, ++maxId);
                var serializedModel = ScanModelForAssociations(model, storageSets, JsonUtil.Serialize(model));
                BlazorDBInterop.SetItem(name, serializedModel, false);
            }

            return guids;
        }

        //TODO: Move this to metadata
        private static int GetMaxId(IEnumerator enumerator)
        {
            var max = 0;
            while (enumerator.MoveNext())
            {
                var model = enumerator.Current;
                var id = GetId(model);
                if (id > max) max = id;
            }

            enumerator.Reset();
            return max;
        }

        private static void DeleteOldModelsFromStorage(Metadata metadata, string storageTableName)
        {
            foreach (var guid in metadata.Guids)
            {
                var name = $"{storageTableName}-{guid}";
                BlazorDBInterop.RemoveItem(name, false);
            }
        }

        private static string ScanModelForAssociations(object model, List<PropertyInfo> storageSets,
            string serializedModel)
        {
            var result = serializedModel;
            foreach (var prop in model.GetType().GetProperties())
            {
                if (prop.GetValue(model) == null || !StorageManagerUtil.IsListInContext(storageSets, prop) ||
                    !StorageManagerUtil.IsInContext(storageSets, prop)) continue;
                if (StorageManagerUtil.IsInContext(storageSets, prop)) result = FixOneAssocaition(model, prop, result);
                if (StorageManagerUtil.IsListInContext(storageSets, prop))
                    result = FixManyAssociation(model, prop, result);
            }

            return result;
        }

        private static string FixManyAssociation(object model, PropertyInfo prop, string result)
        {
            var modelList = (IEnumerable) prop.GetValue(model);
            foreach (var item in modelList)
            {
                var idProp = GetIdProperty(item);
                var id = Convert.ToString(idProp.GetValue(item));
                var serializedItem = JsonUtil.Serialize(item);
                result = ReplaceModelWithId(result, serializedItem, id);
            }

            return result;
        }

        private static string FixOneAssocaition(object model, PropertyInfo prop, string result)
        {
            var associatedModel = prop.GetValue(model);
            var idProp = GetIdProperty(associatedModel);
            var id = Convert.ToString(idProp.GetValue(associatedModel));
            var serializedItem = JsonUtil.Serialize(model);
            result = ReplaceModelWithId(result, serializedItem, id);
            return result;
        }

        private static string ReplaceModelWithId(string result, string serializedItem, string id)
        {
            return result.Replace(serializedItem, id);
        }

        private static int GetId(object item)
        {
            var prop = GetIdProperty(item);
            return (int) prop.GetValue(item);
        }

        private static void SetId(object item, int id)
        {
            var prop = GetIdProperty(item);
            prop.SetValue(item, id);
        }

        private static PropertyInfo GetIdProperty(object item)
        {
            var prop = item.GetType().GetProperty(StorageManagerUtil.Id);
            if (prop == null) throw new ArgumentException("Model must have an Id property");
            return prop;
        }
    }
}