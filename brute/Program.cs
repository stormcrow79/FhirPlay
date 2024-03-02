using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using System.Text;

var settings = new SerializerSettings { Pretty = true };

{
    var role = new PractitionerRole();
    role.Location.Add(new ResourceReference
    {
        Identifier = new Identifier
        {
            System = "https://github.com/synthetichealth/synthea",
            Value = "b675cf86-98df-33c1-8f10-95fd27b53d91"
        },
        Display = "ALL-ACCESS PHYSICAL THERAPY"
    });
    File.WriteAllText(@".\role.json", new FhirJsonSerializer(settings).SerializeToString(role));
    return;
}

var guid = Guid.Parse("5A833725-5E50-40CE-A3F7-8EED6638D11A");
Console.WriteLine(Encoding.GetEncoding(1252).GetString(guid.ToByteArray()));
Console.WriteLine("Zƒ7%^P@Î£÷Žíf8Ñ");

var bytes = Convert.FromBase64String("uNeJRYsPTbawLRkvDGXzXQ==");
//var bytes = Convert.FromBase64String("81A6GgMQQsaqT4vm932edA==");
//var bytes = Convert.FromBase64String("1lme8+1UQRiYq8KFur647w==");
foreach (var val in bytes) Console.Write($"{val:X2}"); // D6599EF3-ED54-4118-98AB-C285BABEB8EF
Console.WriteLine();
Console.WriteLine(new Guid(bytes));
// f39e59d6-54ed-1841-98ab-c285babeb8ef

var filename = @".\practitioner_role.json";
var bundle = new FhirJsonParser().Parse<Bundle>(File.ReadAllText(filename));

Console.WriteLine($"{bundle.Entry.Count()} total entries");

foreach (var entry in bundle.Entry)
{
    var role = (PractitionerRole)entry.Resource;
    var snomed = role.Specialty.SelectMany(c => c.Coding).Where(c => c.System != null).ToArray();
    var identifier = role.Identifier.FirstOrDefault(i => i.System == "http://ns.electronichealth.net.au/id/medicare-provider-number")?.Value;
    if (snomed.Length > 1)
        Console.WriteLine($"{entry.Resource.Id}\t{identifier}");
}

Console.WriteLine("done!");
