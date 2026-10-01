using NineToOne.Accounts.Oidc;
await OidcConsumerSpecs.RunAsync();
await IdentityModelSpecs.RunAsync();
await BoundedIssuerKeysSpecs.RunAsync();
await PreverificationAdmissionSpecs.RunAsync();
Console.WriteLine("PASS ASTRA_AUTH_SYNTHETIC_ALL zero-skips");
