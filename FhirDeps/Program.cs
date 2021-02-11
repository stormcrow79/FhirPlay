extern alias STU3;
extern alias R4;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;


namespace FhirDeps
{
    class Program
    {
        static void Main(string[] args)
        {
            var client3 = new STU3::Hl7.Fhir.Rest.FhirClient(
                "https://securemessaging.dev.telstrahealth.com/epd/stu3/fhir");
            var p3 = new SearchParams();
            p3.Add("family", "Ellison");
            var b3 = client3.Search<STU3::Hl7.Fhir.Model.Practitioner>(p3);

            var client4 = new R4::Hl7.Fhir.Rest.FhirClient(
                "https://sandbox.digitalhealth.gov.au/FhirServerR4-PDA/fhir");
            var p4 = new SearchParams();
            p4.Add("family", "Kidman");
            var b4 = client3.Search<R4::Hl7.Fhir.Model.Practitioner>(p4);
        }
    }
}
