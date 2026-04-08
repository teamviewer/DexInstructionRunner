using System.Collections.Generic;
using System.Linq;

public static class FilterSortHelper
{
    // Sort a list by any column dynamically (ascending or descending)
    public static List<T> ApplySorting<T>(List<T> data, string sortColumn, bool ascending)
    {
        var propInfo = typeof(T).GetProperty(sortColumn);
        if (propInfo == null) return data;

        return ascending
            ? data.OrderBy(x => propInfo.GetValue(x, null)).ToList()
            : data.OrderByDescending(x => propInfo.GetValue(x, null)).ToList();
    }

    // Apply filtering to any list based on column, operator, and value
    public static List<T> ApplyFiltering<T>(List<T> data, string filterColumn, string filterOperator, string filterValue)
    {
        var propInfo = typeof(T).GetProperty(filterColumn);
        if (propInfo == null) return data;

        double filterValNumeric;
        bool isNumeric = double.TryParse(filterValue, out filterValNumeric);

        return data.Where(item =>
        {
            var value = propInfo.GetValue(item, null);
            if (value == null) return false;

            if (isNumeric && double.TryParse(value.ToString(), out var itemNumeric))
            {
                return filterOperator switch
                {
                    "=" => itemNumeric == filterValNumeric,
                    "!=" => itemNumeric != filterValNumeric,
                    "<" => itemNumeric < filterValNumeric,
                    ">" => itemNumeric > filterValNumeric,
                    "<=" => itemNumeric <= filterValNumeric,
                    ">=" => itemNumeric >= filterValNumeric,
                    _ => false
                };
            }
            else
            {
                return filterOperator switch
                {
                    "=" => value.ToString() == filterValue,
                    "!=" => value.ToString() != filterValue,
                    _ => false
                };
            }
        }).ToList();
    }
}
