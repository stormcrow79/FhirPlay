using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;

namespace FhirDeps
{
    class Program
    {
        static FhirClient CreateClient()
        {
            var clientCertificate = new X509Certificate2(
                @"C:\Ccare\Development\Certificates\NASH\Millennium Health Service - 8003623233353381.p12",
                "Pass-123");

            var handler = new WebRequestHandler();
            handler.ClientCertificates.Add(clientCertificate);

            return new FhirClient(
                "https://sandbox.digitalhealth.gov.au/FhirServerR4-PDA/fhir/",
                messageHandler: handler);
        }

        static void FetchPractitionerRole()
        {
            Console.WriteLine("\r\n*** PractitionerRole ***\r\n");
            var client = CreateClient();

            var searchParams = new SearchParams();
            //searchParams.Add("practitioner.name", "kidman");
            searchParams.Add("_id", "97784e9f6c7a489f9d23a40a43161e14");

            searchParams.Include("PractitionerRole:service", IncludeModifier.None);
            searchParams.Include("PractitionerRole:practitioner", IncludeModifier.None);
            searchParams.Include("PractitionerRole:location", IncludeModifier.None);
            searchParams.Include("PractitionerRole:endpoint", IncludeModifier.None);

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

            searchParams.Include("HealthcareService:organization", IncludeModifier.None);
            searchParams.Include("HealthcareService:location", IncludeModifier.None);
            searchParams.Include("HealthcareService:endpoint", IncludeModifier.None);

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

        static void TestValidate()
        {
            var client = new FhirClient(
                "https://stu3.ontoserver.csiro.au/fhir", // correct
                //"https://stu3.ontoserver.csiro.au/fhir/CodeSystem/$lookup?_format=json", // incorrect - dunno how/why it works
                new FhirClientSettings() { Timeout = 10000 });

            try
            {
                // before
                if (false)
                {
                    var query = new Parameters()
                      .Add("url", new FhirUri("http://snomed.info/sct?fhir_vs"))
                      .Add("system", new FhirString("http://snomed.info/sct"))
                      .Add("code", new FhirString("16373008"))
                      .Add("count", new Integer(3))
                      .Add("offset", new Integer(0))
                      .Add("includeDesignations", new FhirBoolean(true));

                    var result = client.TypeOperation<Parameters>("expand", query, true) as Parameters;
                    Console.WriteLine($"validate: {result.Parameter?.FirstOrDefault(x => x.Name == "display")?.Value?.ToString()}");
                }

                // after
                {
                    var result = client.ConceptLookup(
                        new Code("16373008"),
                        new FhirUri("http://snomed.info/sct"));
                    Console.WriteLine($"validate: {result.GetSingleValue<FhirString>("display")}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        static void TestExpand()
        {
            // https://stu3.ontoserver.csiro.au/fhir - 20210228
            // https://r4.ontoserver.csiro.au/fhir - FAIL

            var client = new FhirClient(
                "https://r4.ontoserver.csiro.au/fhir/",
                new FhirClientSettings() { Timeout = 10000 });

            try
            {
                if (true)
                {
                    var parameter = new Parameters()
                        .Add("url", new FhirUri("http://snomed.info/sct?fhir_vs=ecl/<394658006"))
                        .Add("count", new Integer(1000))
                        .Add("activeOnly", new FhirBoolean(true));
                    var valueSet = client.TypeOperation<ValueSet>("expand", parameter, true) as ValueSet;

                    var inactive = valueSet.Expansion.Contains.Where(c => c.Inactive == true).ToArray();
                    Console.WriteLine($"expand: {valueSet.Expansion.Contains.Count} ({inactive.Length} inactive)");
                    foreach (var concept in valueSet.Expansion.Contains.Take(10))
                        Console.WriteLine($"{concept.Code}\t{concept.Display}");
                }

                if (true)
                {
                    var valueSet = client.ExpandValueSet(
                        new FhirUri("http://snomed.info/sct?fhir_vs=ecl/<394658006"));

                    var inactive = valueSet.Expansion.Contains.Where(c => c.Inactive == true).ToArray();
                    Console.WriteLine($"expand: {valueSet.Expansion.Contains.Count} ({inactive.Length} inactive)");
                    foreach (var concept in valueSet.Expansion.Contains.Take(10))
                        Console.WriteLine($"{concept.Code}\t{concept.Display}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            //TestValidate();
            TestExpand();

            //FetchPractitionerRole();
            //FetchHealthcareService();

            //SearchPractitionerRole();
            //TestExpand();
        }

        private static T FindResource<T>(Bundle bundle, ResourceReference reference) where T : Resource =>
            reference == null 
                ? null 
                : bundle.Entry
                    .Where(entry => $"{entry.Resource.TypeName}/{entry.Resource.Id}" == reference.Reference) // FIXME
                    .Select(x => x.Resource)
                    .OfType<T>()
                    .FirstOrDefault();
    }
}
