using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace FhirDeps
{
    class Program
    {
        static FhirClient CreateClient(string serverUrl = null)
        {
            var clientCertificate = new X509Certificate2(
                @"C:\Ccare\Development\Certificates\NASH\Millennium Health Service - 8003623233353381.p12",
                "Pass-123");

            // use WebRequestHandler for FHIR 2.x and newer
            //var handler = new WebRequestHandler();
            //handler.ClientCertificates.Add(clientCertificate);

            var client = new FhirClient(serverUrl ??
                "https://sandbox.digitalhealth.gov.au/FhirServerR4-PDA/fhir/");

            // use OnBeforeRequest for FHIR 1.9 and prior
            client.OnBeforeRequest += (sender, e) =>
                e.RawRequest.ClientCertificates.Add(clientCertificate);
            
            return client;
        }

        private static Bundle PagedSearch<TResource>(
            FhirClient client,
            SearchParams searchParams)
            where TResource : Resource
        {
            var bundle = client.Search<TResource>(searchParams);

            if (bundle.NextLink == null)
                return bundle;

            // keep reading pages until we get to the end, or we hit our result limit
            var nextUri = bundle.NextLink;
            var pageIndex = 2;
            while (nextUri != null)
            {
                Thread.Sleep(1000);
                // TODO: use ILogger
                Debug.WriteLine($"retrieving search result page {pageIndex++} via {nextUri}");

                var page = client.Get(nextUri) as Bundle
                           ?? throw new InvalidOperationException("search returned other than Bundle");

                var matches = page.Entry.Where(e => e.Search.Mode == Bundle.SearchEntryMode.Match).ToArray();

                Debug.WriteLine($"{matches.Length} matches ({page.Entry.Count} total)");

                foreach (var entry in page.Entry)
                {
                    if (entry.Search.Mode == Bundle.SearchEntryMode.Match ||
                        !bundle.Entry.Exists(e => e.Resource.TypeName == entry.Resource.TypeName && e.Resource.Id == entry.Resource.Id))
                        bundle.Entry.Add(entry);
                }

                nextUri = page.NextLink;
            }

            Debug.WriteLine($"TOTAL: {bundle.Entry.Count(e => e.Search.Mode == Bundle.SearchEntryMode.Match)} matches ({bundle.Entry.Count} total)");

            // We're re-using the first Bundle returned by the server, so the
            // header could be slightly incorrect - but since it's internal
            // to this class, there's no point patching it up here.
            return bundle;
        }

        static void FetchAllRoles()
        {
            var client = CreateClient("https://waaseuatfhiraggregator.azurewebsites.net/fhiraggregator/fhir/");

            var searchParams = new SearchParams()
                .Add("active", "true")
                .Include("PractitionerRole:service")
                .Include("PractitionerRole:organization")
                .Include("PractitionerRole:practitioner")
                .Include("PractitionerRole:location")
                .Include("PractitionerRole:endpoint");

            // provider: name, specialty
            // service: name, type, suburb, state, postcode
            // endpoint: connectionType
            var result = PagedSearch<PractitionerRole>(
                client, 
                searchParams);

            using (var writer = new StreamWriter(@"c:\ccare\epd-r4-uat.csv"))
            {
                writer.WriteLine("familyName,givenNames,specialty,serviceName,serviceType,suburb,state,postcode,connectionType");

                var matches = result.Entry.Where(e => e.Search.Mode == Bundle.SearchEntryMode.Match).ToArray();
                foreach (var entry in matches)
                {
                    var role = (PractitionerRole) entry.Resource;
                    var service = FindResource<HealthcareService>(result, role.HealthcareService.FirstOrDefault());
                    var location = FindResource<Location>(result, role.Location.FirstOrDefault());
                    var practitioner = FindResource<Practitioner>(result, role.Practitioner);
                    var endpoint = FindResource<Endpoint>(result, role.Endpoint.FirstOrDefault());

                    var name = practitioner.Name.FirstOrDefault();

                    writer.WriteLine(
                        $"{name?.Family},{string.Join(" ", name?.Given)},{role.Specialty.SelectMany(s => s.Coding).FirstOrDefault()?.Display}," +
                        $"{service?.Name},{service.Type.SelectMany(s => s.Coding).FirstOrDefault()?.Display}," +
                        $"{location?.Address.City},{location?.Address.State},{location?.Address.PostalCode}," +
                        $"{endpoint?.ConnectionType.Code}");
                }
            }

        }

        static void FetchPractitionerRole()
        {
            Console.WriteLine("\r\n*** PractitionerRole ***\r\n");
            var client = CreateClient();

            var searchParams = new SearchParams();
            searchParams.Add("practitioner.name", "kidman");

            searchParams.Include("PractitionerRole:service");
            searchParams.Include("PractitionerRole:practitioner");
            searchParams.Include("PractitionerRole:location");
            searchParams.Include("PractitionerRole:endpoint");

            var bundle = client.Search<PractitionerRole>(searchParams);

            var results = bundle.Entry
                .Where(e => e.Search.Mode == Bundle.SearchEntryMode.Match)
                .Select(bundleEntry =>
                {
                    var role = bundleEntry.Resource as PractitionerRole;
                    var endpointUrl = ResourceReferenceExtensions.GetAbsoluteUriForReference(role.Endpoint.First(), bundleEntry.FullUrl);

                    var service = FindResource<HealthcareService>(bundle, role.HealthcareService.FirstOrDefault());
                    var location = FindResource<Location>(bundle, role.Location.FirstOrDefault());
                    var practitioner = FindResource<Practitioner>(bundle, role.Practitioner);
                    var endpoint = FindResource<Endpoint>(bundle, role.Endpoint.FirstOrDefault());

                    return new
                    {
                        Name = role.Practitioner.Display,
                        Surname = null as string,
                        Speciality = role.Specialty.FirstOrDefault()?.Text,
                        TypeName = service?.Type.FirstOrDefault()?.Text,
                        Organisation = role.Organization.Display,
                        Source = endpoint?.ManagingOrganization.Display,
                        ConnectivityType = endpoint?.ConnectionType.Code,
                        Location = location?.Name,
                        ProviderDirectoryUrl = bundleEntry.FullUrl
                    };
                })
                .ToArray();

            foreach (var result in results)
                Console.WriteLine(JsonConvert.SerializeObject(result));
        }

        static void FetchHealthcareService()
        {
            Console.WriteLine("\r\n*** HealthcareService ***\r\n");
            var client = CreateClient();

            var searchParams = new SearchParams();
            searchParams.Add("name", "fernside");

            searchParams.Include("HealthcareService:organization");
            searchParams.Include("HealthcareService:location");
            searchParams.Include("HealthcareService:endpoint");

            var bundle = client.Search<HealthcareService>(searchParams);

            var results = bundle.Entry
                .Where(e => e.Search.Mode == Bundle.SearchEntryMode.Match)
                .Select(bundleEntry =>
                {
                    var service = bundleEntry.Resource as HealthcareService;
                    var endpointUrl = ResourceReferenceExtensions.GetAbsoluteUriForReference(service.Endpoint.First(), bundleEntry.FullUrl);

                    var location = FindResource<Location>(bundle, service.Location.FirstOrDefault());
                    var endpoint = FindResource<Endpoint>(bundle, service.Endpoint.FirstOrDefault());
                    var organization = FindResource<Organization>(bundle, service.ProvidedBy);

                    return new
                    {
                        Name = service.Name,
                        Surname = null as string,
                        Speciality = service.Specialty.FirstOrDefault()?.Text,
                        TypeName = service.Type.FirstOrDefault()?.Text,
                        Organisation = organization?.Name,
                        Source = endpoint?.ManagingOrganization.Display,
                        ConnectivityType = endpoint?.ConnectionType.Code,
                        Location = location?.Name,
                        ProviderDirectoryUrl = bundleEntry.FullUrl
                    };
                })
                .ToArray();

            foreach (var result in results)
                Console.WriteLine(JsonConvert.SerializeObject(result));
        }

        static void SearchPractitionerRole()
        {
            var client = CreateClient();

            {
                var sp = new SearchParams();

                //sp.Add("")

                var bundle = client.Search<PractitionerRole>(sp);
                foreach (var role in bundle.GetResources().OfType<PractitionerRole>())
                    Console.WriteLine(role.Practitioner.Display);
            }
        }

        static void TestExpand()
        {
            //var json = new WebClient().DownloadString("https://r4.ontoserver.csiro.au/fhir/ValueSet/$expand?url=http://snomed.info/sct?fhir_vs=ecl/%3C394658006");

            // http://snomed.info/sct?fhir_vs=ecl%2F%3C394658006&count=1000&activeOnly=true
            var client = new FhirClient(
                "https://r4.ontoserver.csiro.au/fhir/");

            var p = new Parameters()
                .Add("url", new FhirUri("http://snomed.info/sct?fhir_vs=ecl/<394658006"))
                .Add("count", new Integer(1000))
                .Add("activeOnly", new FhirBoolean(true));
            var result = client.TypeOperation<ValueSet>("expand", p, true);

        }

        static string token = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsIng1dCI6IjJaUXBKM1VwYmpBWVhZR2FYRUpsOGxWMFRPSSIsImtpZCI6IjJaUXBKM1VwYmpBWVhZR2FYRUpsOGxWMFRPSSJ9.eyJhdWQiOiJodHRwczovL25wZC1vaWRjIiwiaXNzIjoiaHR0cHM6Ly9zdHMud2luZG93cy5uZXQvMTI2MmJhMDItMjg0MS00NGZmLThjYzctZjk1ODE5M2ExNzI3LyIsImlhdCI6MTY2ODA2NjM0NiwibmJmIjoxNjY4MDY2MzQ2LCJleHAiOjE2NjgwNzAyNDYsImFpbyI6IkUyWmdZSmhUYlozRnIvT3BmTUtDNUcvSFpqdzJCd0E9IiwiYXBwaWQiOiJiYWIyZGI2Yy1lMzRmLTQ0MDItODg5Yi0wODMzNjI3Zjc0OTciLCJhcHBpZGFjciI6IjEiLCJpZHAiOiJodHRwczovL3N0cy53aW5kb3dzLm5ldC8xMjYyYmEwMi0yODQxLTQ0ZmYtOGNjNy1mOTU4MTkzYTE3MjcvIiwib2lkIjoiYjlkOGQ5NjgtNmU4ZS00YWU4LWI1MGUtMjE0ODkyZjQ3YzZkIiwicmgiOiIwLkFXWUFBcnBpRWtFb18wU014X2xZR1RvWEo2R3d1djhzLWFsQXR6UjN0RFppamxkbUFBQS4iLCJyb2xlcyI6WyJURVJNX0RFVl9GSElSIl0sInN1YiI6ImI5ZDhkOTY4LTZlOGUtNGFlOC1iNTBlLTIxNDg5MmY0N2M2ZCIsInRpZCI6IjEyNjJiYTAyLTI4NDEtNDRmZi04Y2M3LWY5NTgxOTNhMTcyNyIsInV0aSI6IkF0by1JRHVlcEUyUGdXWFhTanhFQVEiLCJ2ZXIiOiIxLjAifQ.uk9dVwSK00WtuD1simiqtgug_H2L1XUfmHf6Kj68KuI3PRl2BPacn8dpLN324_f8DKKAE02dYZMkGQlMKLGhck72635rk_PVBVpqRUkCgSTmnEOCRsZOg4_TAGbSuL3hRZYXrWAHOtYocWDcBA6VNNoAAcdkibwT264FDq5SYBxlKzg82Co8Sw1xzEGVMJOUhoy_Upbsqock3Fp4rVLWPV3fERKBBmBPppOStnfaNBiPYf2qIE0-pAAsxKH1eFm1TaajS0IWc0lkRzknfzE1-WBhtzvoeMqerK2c7DgPv8dbzxft4BE-EwKCk0CreHVgqCqcjxQGe01RWjA4DRNAcA";

        static void TestLookup()
        {
            //var endpoint = "https://r4.ontoserver.csiro.au/fhir/";
            //var endpoint = "https://api.healthterminologies.gov.au/integration/R4/fhir";
            var endpoint = "https://api.dev.telstrahealth.com/terminology/fhir";
            
            var client = new FhirClient(endpoint);
            client.OnBeforeRequest += (sender, e) =>
            {
                if (token != null)
                    e.RawRequest.Headers.Add("Authorization", "Bearer " + token);
            };

            var p = new Parameters()
                .Add("code", new FhirString("865922007"))
                .Add("system", new FhirUri("http://snomed.info/sct"));
            var result = client.TypeOperation<CodeSystem>("lookup", p) as Parameters;
            Console.WriteLine(result.Parameter.FirstOrDefault(x => x.Name == "display")?.Value?.ToString());
        }

        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            FetchAllRoles();

            //FetchPractitionerRole();
            //FetchHealthcareService();

            //SearchPractitionerRole();
            //TestExpand();

            TestLookup();
        }

        private static T FindResource<T>(Bundle bundle, ResourceReference reference) where T : Resource =>
            reference == null
                ? null :
                bundle.Entry
                    .Where(entry => $"{entry.Resource.TypeName}/{entry.Resource.Id}" == reference.Reference) // FIXME
                    .Select(x => x.Resource)
                    .OfType<T>()
                    .FirstOrDefault();
    }
}
