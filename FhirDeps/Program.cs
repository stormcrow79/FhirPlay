using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;

namespace FhirDeps
{
    class Program
    {
        static FhirClient CreateClient(string serverUrl = null, string apiKey = null)
        {
            var client = new FhirClient(serverUrl)
            {
                PreferredFormat = ResourceFormat.Json,
            };
            client.OnBeforeRequest += (sender, e) => e.RawRequest.Headers.Add("x-api-key", apiKey);

            return client;
        }

        private static IEnumerable<Bundle.EntryComponent> Matches(Bundle bundle) =>
            bundle.Entry.Where(e => e.Search.Mode == Bundle.SearchEntryMode.Match);

        private static bool Exists(Bundle bundle, string type, string id) =>
            bundle.Entry.Exists(e => e.Resource.TypeName == type && e.Resource.Id == id);

        private static SearchParams PatchSearchParams(SearchParams original, Uri continuationUrl)
        {
            var parameters = continuationUrl.Query
                .Trim('?')
                .Split('&')
                .Select(s =>
                {
                    var x = s.Split('=');
                    return Tuple.Create(x[0], x[1]);
                });
            var continuationParams = SearchParams.FromUriParamList(parameters);

            foreach (var originalInclude in original.Include)
            {
                if (!continuationParams.Include.Contains(originalInclude))
                    continuationParams.Include.Add(originalInclude);
            }

            return continuationParams;
        }

        private static void ValidateBundle(Bundle bundle)
        {
            foreach (var role in Matches(bundle).Select(e => e.Resource).OfType<PractitionerRole>())
            {
                var identity = new ResourceIdentity(role.Practitioner.Reference);
                if (!Exists(bundle, identity.ResourceType, identity.Id))
                    Console.WriteLine($"    broken link PractitionerRole/{role.Id} -> {identity}");
            }
        }

        private static (Bundle Bundle, Bundle.EntryComponent[] Matches) PagedSearch<TResource>(
            FhirClient client,
            SearchParams searchParams,
            int maximumResultCount = int.MaxValue)
            where TResource : Resource
        {
            var pageIndex = 1;
            Debug.WriteLine($"retrieving search result page {pageIndex++}");

            var bundle = client.Search<TResource>(searchParams);

            Debug.WriteLine($"  matches: {bundle.Entry.Count(x => x.Search.Mode == Bundle.SearchEntryMode.Match)}");
            Debug.WriteLine($"  includes: {bundle.Entry.Count(x => x.Search.Mode == Bundle.SearchEntryMode.Include)}");

            if (bundle.NextLink == null)
                return (bundle, Matches(bundle).ToArray());

            // keep reading pages until we get to the end, or we hit our result limit
            var nextUri = bundle.NextLink;
            while (nextUri != null && Matches(bundle).Count() <= maximumResultCount)
            {
                // TODO: use ILogger
                Debug.WriteLine($"retrieving search result page {pageIndex++} via {nextUri}");

                var continationParams = PatchSearchParams(searchParams, nextUri);
                var page = client.Search<TResource>(continationParams);

                //var page = client.Get(nextUri) as Bundle
                //           ?? throw new InvalidOperationException("search returned other than Bundle");

                Debug.WriteLine($"  matches: {page.Entry.Count(x => x.Search.Mode == Bundle.SearchEntryMode.Match)}");
                Debug.WriteLine($"  includes: {page.Entry.Count(x => x.Search.Mode == Bundle.SearchEntryMode.Include)}");

                ValidateBundle(page);

                foreach (var entry in page.Entry)
                {
                    if (entry.Search.Mode == Bundle.SearchEntryMode.Match ||
                        !Exists(bundle, entry.Resource.TypeName, entry.Resource.Id))
                        bundle.Entry.Add(entry);
                }

                nextUri = page.NextLink;
            }

            // We're re-using the first Bundle returned by the server, so the
            // header could be slightly incorrect - but since it's internal
            // to this class, there's no point patching it up here.
            return (bundle, Matches(bundle).ToArray());
        }

        static void HealthlinkPaging(string apiKey)
        {
            Console.WriteLine("\r\n*** HealthlinkPaging ***\r\n");
            var client = CreateClient("https://api.healthlink.net/directory/v2", apiKey);

            var searchParams = new SearchParams();
            searchParams.Add("practitioner.name", "christian");
            //searchParams.Add("size", "2000");
            searchParams.Count = 5;

            //searchParams.Include("PractitionerRole:service");
            //searchParams.Include("PractitionerRole:organization");
            searchParams.Include("PractitionerRole:practitioner");
            //searchParams.Include("PractitionerRole:location");
            //searchParams.Include("PractitionerRole:endpoint");

            var (bundle, matches) = PagedSearch<PractitionerRole>(client, searchParams);

            var serializer = new Hl7.Fhir.Serialization.FhirJsonSerializer();
            File.WriteAllText("bundle.json", serializer.SerializeToString(bundle));

            return;
        }

        static void Main(string[] args)
        {
            Debug.Listeners.Add(new ConsoleTraceListener());
            HealthlinkPaging(args[0]);
        }

        private static T FindResource<T>(Bundle bundle, ResourceReference reference) where T : Resource =>
            reference == null
                ? null :
                bundle.Entry
                    .Where(entry => $"{ entry.Resource.TypeName}/{entry.Resource.Id}" == reference.Reference) // FIXME
                    .Select(x => x.Resource)
                    .OfType<T>()
                    .FirstOrDefault();
    }
}
