﻿namespace RMMobileApp.Converters
{
    public class ProductImageUrlConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if(value is string baseUrl)
            {
                return MobileConfiguration.ApiUrl + baseUrl;
            }

            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
