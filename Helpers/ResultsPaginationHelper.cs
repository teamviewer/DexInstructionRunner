using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

public class ResultsPaginationHelper
{
    private readonly Action<List<JObject>> _renderAction;
    private List<JObject> _allItems = new();
    private List<JObject> _filteredItems = new();
    private int _pageSize = 50;
    private int _currentPage = 1;
    private int _totalPages = 1;

    public ResultsPaginationHelper(Action<List<JObject>> renderAction)
    {
        _renderAction = renderAction;
    }

    public void SetItems(List<JObject> items)
    {
        _allItems = items;
        _filteredItems = items;
        _currentPage = 1;
        CalculatePages();
        RenderCurrentPage();
    }

    public void SetPageSize(int size)
    {
        _pageSize = size;
        _currentPage = 1;
        CalculatePages();
        RenderCurrentPage();
    }

    public void Sort(string sortOption)
    {
        _filteredItems = sortOption switch
        {
            "FQDN Asc" => _filteredItems.OrderBy(r => r["FQDN"]?.ToString()).ToList(),
            "FQDN Desc" => _filteredItems.OrderByDescending(r => r["FQDN"]?.ToString()).ToList(),
            "Timestamp Asc" => _filteredItems.OrderBy(r => r["Timestamp"]?.ToObject<DateTime?>()).ToList(),
            "Timestamp Desc" => _filteredItems.OrderByDescending(r => r["Timestamp"]?.ToObject<DateTime?>()).ToList(),
            _ => _filteredItems
        };
        _currentPage = 1;
        RenderCurrentPage();
    }

    public void NextPage()
    {
        if (_currentPage < _totalPages)
        {
            _currentPage++;
            RenderCurrentPage();
        }
    }

    public void PreviousPage()
    {
        if (_currentPage > 1)
        {
            _currentPage--;
            RenderCurrentPage();
        }
    }

    private void CalculatePages()
    {
        _totalPages = (int)Math.Ceiling((double)_filteredItems.Count / _pageSize);
    }

    private void RenderCurrentPage()
    {
        var page = _filteredItems
            .Skip((_currentPage - 1) * _pageSize)
            .Take(_pageSize)
            .ToList();

        _renderAction.Invoke(page);
    }

    public int CurrentPage => _currentPage;
    public int TotalPages => _totalPages;
}
