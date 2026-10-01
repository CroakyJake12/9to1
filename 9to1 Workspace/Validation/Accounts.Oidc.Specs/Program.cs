using NineToOne.Accounts.Oidc;
await OidcConsumerSpecs.RunAsync();
await IdentityModelSpecs.RunAsync();
await BoundedIssuerKeysSpecs.RunAsync();
await PreverificationAdmissionSpecs.RunAsync();
await NineToOne.Accounts.Remote.WorkerAccountApiClientSpecs.RunAsync();
Console.WriteLine("PASS ASTRA_ACCOUNT_API_SYNTHETIC_ALL zero-skips");
