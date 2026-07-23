using System;
using System.Collections.Generic;
using CmsSlugs;
using CmsSlugs.Optimizely;
using EPiServer.Web.Mvc;
using Microsoft.AspNetCore.Mvc;
using Website.Test.Models.Pages;

namespace Website.Test.Controllers;

public class StartPageController : PageController<StartPage>
{
    private readonly ISlugStore _store;
    private readonly ISlugSource _slugSource;

    public StartPageController(ISlugStore store, ISlugSource slugSource)
    {
        _store = store;
        _slugSource = slugSource;
    }

    public IActionResult Index(StartPage currentPage)
    {
        ViewData["SlugsReady"] = _store.IsReady;
        ViewData["SlugCount"] = _store.Count;

        // Only enumerate once the index is built; a not-ready store may be mid-boot or empty.
        ViewData["SlugExamples"] = _store.IsReady
            ? PickRandom(AllEntries(), 5)
            : Array.Empty<SlugEntry>();

        return View(currentPage);
    }

    // Flatten the source the same way a full build does (list links, resolve each), so example
    // slugs match what gets indexed without needing a separate GetAll on the source.
    private IEnumerable<SlugEntry> AllEntries()
    {
        foreach (var link in _slugSource.GetContentLinks())
            foreach (var entry in _slugSource.Resolve(link))
                yield return entry;
    }

    // Reservoir sampling: one pass, O(count) memory, so we don't materialise a large index.
    private static IReadOnlyList<SlugEntry> PickRandom(IEnumerable<SlugEntry> source, int count)
    {
        var reservoir = new List<SlugEntry>(count);
        var rng = Random.Shared;
        var i = 0;
        foreach (var entry in source)
        {
            if (reservoir.Count < count)
            {
                reservoir.Add(entry);
            }
            else
            {
                var j = rng.Next(i + 1);
                if (j < count)
                    reservoir[j] = entry;
            }
            i++;
        }
        return reservoir;
    }
}
