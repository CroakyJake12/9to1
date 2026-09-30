using System.Text.Json;
using NineToOne.Os.Shell.Authority;
namespace NineToOne.Os.Shell.Tests;
public sealed class InstallationReceiptInteropTests
{
    [Fact]
    public void RealMetadataProducerEnvelopeVerifiesWithExplicitFixtureOnlyIssuer()
    {
        // Real metadata-producer fixture; public issuer is explicit test input, never platform trust.
        const string envelope = """
{"issuerKeyId":"fixture.issuer","payload":"eyJhbGxvd2VkU2VydmljZUlkcyI6WyJob21lLnJlYWRpbmVzcyJdLCJhcHBJZCI6ImZpeHR1cmUuYXBwIiwiZGVza3RvcEVudHJ5UGF0aCI6Ii91c3Ivc2hhcmUvYXBwbGljYXRpb25zL2ZpeHR1cmUuYXBwLmRlc2t0b3AiLCJkZXNrdG9wRW50cnlTaGEyNTYiOiJhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhIiwiZW50cnlwb2ludCI6ImRlc2t0b3A6Zml4dHVyZS5hcHAuZGVza3RvcCIsImV4ZWN1dGFibGVQYXRoIjoiL29wdC85dG8xL2FwcHMvZml4dHVyZS5hcHAvZml4dHVyZS1hcHAiLCJmaWxlcyI6W3sicGF0aCI6ImZpeHR1cmUtYXBwIiwic2hhMjU2IjoiMWM3NDcyZDkyM2NmZGNmMjEwOTY5NjJjYTI4NTQwNTNiYjdiMmRjODdiOWVhOTlkYjkxOWJkMzdkZTEwYjBhNSIsInNpemUiOjUxfV0sImluc3RhbGxSb290IjoiL29wdC85dG8xL2FwcHMvZml4dHVyZS5hcHAiLCJvc0FwcGxpY2F0aW9uSWQiOiJkZXNrdG9wOmZpeHR1cmUuYXBwLmRlc2t0b3AiLCJwcm92aWRlcklkIjoibGludXgueGRnLWRlc2t0b3AiLCJyZWNlaXB0UmV2aXNpb24iOjEsInJvbGVzIjpbImZpeHR1cmUucm9sZSJdLCJzY2hlbWFWZXJzaW9uIjoxfQ==","schemaVersion":1,"signature":"k93tynX+za4LLXvWBkYm6DC4zYkKL0xdfwB6kr3x48VH0/Dx2rx8wvnIcuRmZkXQOq5gZzOt3lcuno62Fhe/Zr/9TcYB+JhXaY8A2cye/6Geq9yFtWo5YUWiC/TbSgjMWLcFh8DnqenojHPv7rzEB7nTr6FRXUQP2B51/ANUmDXL04dQh3RuVEVwlJoqq9yCB6cWfxSNUMDrnmbw68KANKMh1neBOu4PacO1b6noR6hbu5g2/+tnLNHlWre7Ydo9TtB54gvXVbV7MNDH9hDCqfPLbtCZ1cvRVyy5ZTWFb0Exe1WcmCwDkI/Qdc71ZvZCoiHy6DEWm+TAWCXu6NrqjkETk4n0HlxDs9bprjUHdFdvA4aHuPxIk5fDlupYgGsLZyHnDGvyVxFfNOHfq9RaJeTHQJ0XEv3d2fGtnoUu02QbwOImGu5+5Genusb6sU+bm4FBgWp7jJ6/7DtIL9YMTBCSU8D0vtZsduoq2sOO44SEIsmnzQv4Un/AAcn1o1+G"}
""";
        var issuer = new PublisherTrust("fixture.issuer", "MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEApqAZ6T1GrzC+chduXoFir07s/7WxADsDZ6PSxYhtVKX9+A5sU2zFKYzJGunVhYbbaOH5Iep+XJtaHhJkm6z3fcd8q7dayMGU59m+Ze7/Js+pcgdwPkPO/bEweDuSaWUJkpRuFwbvvQvjM37ETinCzkoBrVi/zGSTZG6zk6/o1PKici1VtZLs2safh0m5h+yaPhsqtkUs6CNF3vD2ElPBazzhz36dpHn0MlyctCgHb+WhjkjM7OozeyW2d0FgbpHbNKnqT45UXTeaI6jKemH3Bqom1MGlBzId1JcRNF+UHaEwTcERBaNXKBnKOv+PUA0d33ZiOCEz9EEZVOHa70oGzt1Tpk0tpdz3K3FdekUUW8zwWPk37XxNlBtWMfYRKip0+6SmNmRtPkzbrvRYcfk6Y2CVSH6rcLn5SbPWIpC6Vg7DQsXLQm8MO6J5olMhxAuxLGQ4T2HAANLHpVhidHAsAv0Pdymw0Fm5flmw7Y9hmmIixnOvZnvoPVhrapbuMngtAgMBAAE=", ["fixture.app"], ["home.readiness"], ["fixture.role"]);
        var receipt = InstallationReceiptSignature.Verify(System.Text.Encoding.UTF8.GetBytes(envelope), [issuer]);
        Assert.NotNull(receipt); Assert.Equal("fixture.app", receipt.AppId);
        Assert.Equal("linux.xdg-desktop", receipt.ProviderId);
        Assert.Equal("desktop:fixture.app.desktop", receipt.OsApplicationId);
        Assert.Equal(receipt.OsApplicationId, receipt.Entrypoint);
        Assert.Equal("/opt/9to1/apps/fixture.app/fixture-app", receipt.ExecutablePath);
        Assert.NotEqual(receipt.ExecutablePath, receipt.Entrypoint);
        // Exact crypto/schema interoperability does not prove a root-installed package or controlled launch.
        Assert.Null(InstallationReceiptSignature.Verify(System.Text.Encoding.UTF8.GetBytes(envelope), []));
    }
}
