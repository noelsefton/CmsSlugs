using System;
using System.Collections.Generic;
using CmsSlugs;
using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using Website.Test.Models.Pages;

namespace Website.Test.Controllers;

public class StartPageController : PageController<StartPage>
{
    private readonly ISlugStore _store;

    public StartPageController(ISlugStore store)
    {
        _store = store;
    }

    public IActionResult Index(StartPage currentPage)
    {
        ViewData["SlugsReady"] = _store.IsReady;
        var count = _store.Count;
        ViewData["SlugCount"] = count;

        // Pull a few example slugs straight from the index via the positional indexer — never
        // re-scan the catalog source on a page request.
        var examples = new List<SlugEntry>();
        if (_store.IsReady && count > 0)
        {
            var take = (int)Math.Min(5, count);
            var seen = new HashSet<long>();
            while (examples.Count < take && seen.Count < count)
            {
                var i = Random.Shared.NextInt64(count);
                if (!seen.Add(i)) continue;
                if (_store[i] is { } entry)
                    examples.Add(entry);
            }
        }
        ViewData["SlugExamples"] = examples;

        return View(currentPage);
    }
}
