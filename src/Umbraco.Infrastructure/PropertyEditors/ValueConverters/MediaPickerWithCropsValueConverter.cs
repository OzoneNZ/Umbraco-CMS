using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Models;
using Umbraco.Extensions;

namespace Umbraco.Cms.Core.PropertyEditors.ValueConverters
{
    [DefaultPropertyValueConverter]
    public class MediaPickerWithCropsValueConverter : PropertyValueConverterBase
    {
        private readonly IPublishedSnapshotAccessor _publishedSnapshotAccessor;
        private readonly IPublishedUrlProvider _publishedUrlProvider;
        private readonly IPublishedValueFallback _publishedValueFallback;
        private readonly IJsonSerializer _jsonSerializer;

        public MediaPickerWithCropsValueConverter(
            IPublishedSnapshotAccessor publishedSnapshotAccessor,
            IPublishedUrlProvider publishedUrlProvider,
            IPublishedValueFallback publishedValueFallback,
            IJsonSerializer jsonSerializer)
        {
            _publishedSnapshotAccessor = publishedSnapshotAccessor ?? throw new ArgumentNullException(nameof(publishedSnapshotAccessor));
            _publishedUrlProvider = publishedUrlProvider;
            _publishedValueFallback = publishedValueFallback;
            _jsonSerializer = jsonSerializer;
        }

        public override bool IsConverter(IPublishedPropertyType propertyType) => propertyType.EditorAlias.Equals(Core.Constants.PropertyEditors.Aliases.MediaPicker3);

        public override bool? IsValue(object value, PropertyValueLevel level)
        {
            var isValue = base.IsValue(value, level);
            if (isValue != false && level == PropertyValueLevel.Source)
            {
                // Empty JSON array is not a value
                isValue = value?.ToString() != "[]";
            }

            return isValue;
        }

        public override Type GetPropertyValueType(IPublishedPropertyType propertyType)
            => IsMultipleDataType(propertyType.DataType)
                ? typeof(IEnumerable<MediaWithCrops>)
                : typeof(MediaWithCrops);

        public override PropertyCacheLevel GetPropertyCacheLevel(IPublishedPropertyType propertyType) => PropertyCacheLevel.Snapshot;

        public override object ConvertIntermediateToObject(IPublishedElement owner, IPublishedPropertyType propertyType, PropertyCacheLevel referenceCacheLevel, object inter, bool preview)
        {
            var isMultiple = IsMultipleDataType(propertyType.DataType);
            if (string.IsNullOrEmpty(inter?.ToString()))
            {
                // Short-circuit on empty value
                return isMultiple ? Enumerable.Empty<MediaWithCrops>() : null;
            }

            var mediaItems = new List<MediaWithCrops>();
            var dtos = MediaPicker3PropertyEditor.MediaPicker3PropertyValueEditor.Deserialize(_jsonSerializer, inter);
            var configuration = propertyType.DataType.ConfigurationAs<MediaPicker3Configuration>();
            var publishedSnapshot = _publishedSnapshotAccessor.GetRequiredPublishedSnapshot();
            foreach (var dto in dtos)
            {
                var mediaItem = publishedSnapshot.Media.GetById(preview, dto.MediaKey);
                if (mediaItem != null)
                {
                    var localCrops = new ImageCropperValue
                    {
                        Crops = dto.Crops,
                        FocalPoint = dto.FocalPoint,
                        Src = mediaItem.Url(_publishedUrlProvider)
                    };

                    localCrops.ApplyConfiguration(configuration);

                    // TODO: This should be optimized/cached, as calling Activator.CreateInstance is slow
                    var mediaWithCropsType = typeof(MediaWithCrops<>).MakeGenericType(mediaItem.GetType());
                    var mediaWithCrops = (MediaWithCrops)Activator.CreateInstance(mediaWithCropsType, mediaItem, _publishedValueFallback, localCrops);

                    mediaItems.Add(mediaWithCrops);

                    if (!isMultiple)
                    {
                        // Short-circuit on single item
                        break;
                    }
                }
            }

            return isMultiple ? mediaItems : mediaItems.FirstOrDefault();
        }

        private bool IsMultipleDataType(PublishedDataType dataType) => dataType.ConfigurationAs<MediaPicker3Configuration>().Multiple;
    }
}
