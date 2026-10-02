using NineToOne.Accounts.Oidc;
await OidcConsumerSpecs.RunAsync();
await IdentityModelSpecs.RunAsync();
await BoundedIssuerKeysSpecs.RunAsync();
await NineToOne.Accounts.Remote.WorkerAccountApiClientSpecs.RunAsync();
await RemoteAccountWebFacadeSpecs.RunAsync();
await RetainedProfileIntentSpecs.RunAsync();
Console.WriteLine("PASS ASTRA_ACCOUNTS_REMOTE_LIBRARY_WEB_SYNTHETIC_ALL zero-skips");
