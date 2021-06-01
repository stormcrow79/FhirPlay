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

        static void Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            var ri = new ResourceIdentity("https://sandbox.digitalhealth.gov.au/FhirServerR4-PDA/fhir/PractitionerRole/97784e9f6c7a489f9d23a40a43161e14");

            FetchPractitionerRole();
            FetchHealthcareService();

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
