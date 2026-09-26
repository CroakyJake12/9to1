namespace Haven.Core;
public static class GenUiSemanticValidator
{
 public const int CurrentSchemaVersion = 1;
 public static GenUiSemanticValidationResult ValidateAndRepair(GenUiAppDefinition app)
 {
  ArgumentNullException.ThrowIfNull(app);
  ArgumentNullException.ThrowIfNull(app.Document);
  var errors=GenerativeUiContractValidator.Validate(app.Document).ToList();
  if(app.StateSchema is null||app.DerivedState is null||app.Bindings is null||app.Actions is null||app.ResultSchemas is null||app.ErrorSchemas is null||app.Routes is null||app.Document.State is null||app.Document.Root is null||app.Rendering is null)
  {
   errors.Add("Generated app definition is missing a required schema, route, state, or document collection.");
   return new(errors,[],app);
  }
  if(errors.Count>0) return new(errors,[],app);
  if(string.IsNullOrWhiteSpace(app.AppId)||app.SchemaVersion!=CurrentSchemaVersion||string.IsNullOrWhiteSpace(app.RuntimeVersion)) errors.Add("Generated app identity, schema version, and runtime version are required and supported.");
  var repairs=new List<string>();
  var state=app.StateSchema.Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);
  if(state.Count!=app.StateSchema.Count||state.Contains("")) errors.Add("State field keys must be unique and non-empty.");
  foreach(var field in app.StateSchema)
  {
   if(!Enum.IsDefined(field.Type)||!Enum.IsDefined(field.Persistence)) errors.Add($"State field '{field.Key}' has an unsupported type or persistence scope.");
   if(!string.IsNullOrWhiteSpace(field.Key)&&field.DefaultValue is System.Text.Json.JsonElement defaultElement&&!Matches(defaultElement,field.Type)) errors.Add($"State field '{field.Key}' default does not match {field.Type}.");
   if(!string.IsNullOrWhiteSpace(field.Key)&&app.Document.State.TryGetValue(field.Key,out var value)&&!Matches(value,field.Type)) errors.Add($"State field '{field.Key}' value does not match {field.Type}.");
   if(field.Required&&!app.Document.State.ContainsKey(field.Key)&&field.DefaultValue is null) errors.Add($"Required state field '{field.Key}' has no value or default.");
  }
  var derived=app.DerivedState.Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);
  if(derived.Count!=app.DerivedState.Count||derived.Overlaps(state)) errors.Add("Derived state keys must be unique.");
  foreach(var item in app.DerivedState)
  {
   if(string.IsNullOrWhiteSpace(item.Expression)) errors.Add($"Derived state '{item.Key}' requires an expression.");
   foreach(var dependency in item.Dependencies) if(!state.Contains(dependency)&&!derived.Contains(dependency)) errors.Add($"Derived state '{item.Key}' references unknown dependency '{dependency}'.");
  }
  var components=Flatten(app.Document.Root).Select(x=>x.ComponentId).ToHashSet(StringComparer.Ordinal);
  foreach(var binding in app.Bindings)
  {
   if(!components.Contains(binding.ComponentId)) errors.Add($"Binding references missing component '{binding.ComponentId}'.");
   if(!state.Contains(binding.StateKey)&&!derived.Contains(binding.StateKey)) errors.Add($"Binding references unknown state '{binding.StateKey}'.");
   if(binding.Mode==GenUiBindingMode.TwoWay&&derived.Contains(binding.StateKey)) errors.Add($"Derived state '{binding.StateKey}' cannot be two-way bound.");
  }
  var resultSchemaIds=app.ResultSchemas.Select(x=>x.SchemaId).ToHashSet(StringComparer.Ordinal);
  if(resultSchemaIds.Count!=app.ResultSchemas.Count||resultSchemaIds.Contains("")) errors.Add("Result schema IDs must be unique and non-empty.");
  var errorSchemaIds=app.ErrorSchemas.Select(x=>x.ErrorId).ToHashSet(StringComparer.Ordinal);
  if(errorSchemaIds.Count!=app.ErrorSchemas.Count||errorSchemaIds.Contains("")) errors.Add("Error schema IDs must be unique and non-empty.");
  foreach(var schema in app.ResultSchemas)
  {
   ValidateFields(schema.Fields,$"Result schema '{schema.SchemaId}'",errors);
   if(schema.Fields.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=schema.Fields.Count) errors.Add($"Result schema '{schema.SchemaId}' has duplicate field keys.");
  }
  foreach(var schema in app.ErrorSchemas)
  {
   if(string.IsNullOrWhiteSpace(schema.Code)||string.IsNullOrWhiteSpace(schema.Message)) errors.Add($"Error schema '{schema.ErrorId}' requires a code and message.");
   ValidateFields(schema.Details,$"Error schema '{schema.ErrorId}'",errors);
   if(schema.Details.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=schema.Details.Count) errors.Add($"Error schema '{schema.ErrorId}' has duplicate detail keys.");
  }
  var actionIds=app.Actions.Select(x=>x.ActionId).ToHashSet(StringComparer.Ordinal);
  if(actionIds.Count!=app.Actions.Count||actionIds.Contains("")) errors.Add("Action IDs must be unique and non-empty.");
  var componentActions=Flatten(app.Document.Root).SelectMany(component=>component.Actions).ToArray();
  foreach(var binding in componentActions)
  {
   var definition=app.Actions.FirstOrDefault(action=>action.ActionId==binding.ActionId);
   if(definition is null) errors.Add($"Component action '{binding.ActionId}' has no typed action definition.");
   else if(definition.ExecutionKind!=ExecutionKind(binding.Route)) errors.Add($"Action '{binding.ActionId}' execution kind does not match its component route.");
  }
  foreach(var action in app.Actions)
   if(!componentActions.Any(binding=>binding.ActionId==action.ActionId)) errors.Add($"Typed action '{action.ActionId}' is not bound to a component.");
  foreach(var action in app.Actions)
  {
   if(!Enum.IsDefined(action.ExecutionKind)) errors.Add($"Action '{action.ActionId}' has an unsupported execution kind.");
   ValidateFields(action.Inputs,$"Action '{action.ActionId}' input",errors);
   if(action.Inputs.Select(x=>x.Key).Distinct(StringComparer.Ordinal).Count()!=action.Inputs.Count) errors.Add($"Action '{action.ActionId}' has duplicate input keys.");
   if(action.ResultSchemaId is { } resultId&&!resultSchemaIds.Contains(resultId)) errors.Add($"Action '{action.ActionId}' references unknown result schema '{resultId}'.");
   foreach(var errorId in action.ErrorSchemaIds) if(!errorSchemaIds.Contains(errorId)) errors.Add($"Action '{action.ActionId}' references unknown error schema '{errorId}'.");
  }
  if(app.Rendering.AllowsExecutableCode) errors.Add("Generated executable code is not permitted by the current trusted GenUI runtime.");
  if(app.Rendering.Layer==GenUiRenderingLayer.GeneratedSandbox && app.Document.Origin.TemplateId is not null) errors.Add("GeneratedSandbox rendering is reserved for purpose-built generated definitions without a trusted template ID.");
  var routes=app.Routes.ToList();
  var routeIds=routes.Select(x=>x.RouteId).ToHashSet(StringComparer.Ordinal);
  if(routeIds.Count!=routes.Count) errors.Add("Navigation route IDs must be unique.");
  foreach(var route in routes) if(!components.Contains(route.PageComponentId)) errors.Add($"Route '{route.RouteId}' references missing page component '{route.PageComponentId}'.");
  if(routes.Count>0)
  {
   var starts=routes.Where(x=>x.IsStartRoute).ToArray();
   if(starts.Length==0)
   {
    var i=routes.FindIndex(x=>x.Kind==GenUiNavigationKind.Root); if(i<0)i=0; routes[i]=routes[i] with { IsStartRoute=true }; repairs.Add($"Marked route '{routes[i].RouteId}' as the start route.");
   }
   else if(starts.Length>1) errors.Add("Navigation requires exactly one start route.");
   for(var i=0;i<routes.Count;i++) if(routes[i].ParentRouteId is { } parent&&!routeIds.Contains(parent)) { routes[i]=routes[i] with { ParentRouteId=null }; repairs.Add($"Removed missing parent route '{parent}' from '{routes[i].RouteId}'."); }
   var start=routes.SingleOrDefault(x=>x.IsStartRoute)?.RouteId;
   if(start is not null)
   {
    var reachable=new HashSet<string>(StringComparer.Ordinal){start};
    for(var changed=true;changed;)
    {
     changed=false;
     foreach(var route in routes) if(!reachable.Contains(route.RouteId)&&route.ParentRouteId is { } p&&reachable.Contains(p)) { reachable.Add(route.RouteId); changed=true; }
    }
    foreach(var route in routes.Where(x=>!reachable.Contains(x.RouteId))) errors.Add($"Route '{route.RouteId}' is unreachable from start route '{start}'.");
   }
  }
 return new(errors,repairs,app with { Routes=routes });
 }
 private static GenUiActionExecutionKind? ExecutionKind(GenUiRouteKind route)=>route switch
 {
  GenUiRouteKind.Local=>GenUiActionExecutionKind.Local,
  GenUiRouteKind.App=>GenUiActionExecutionKind.App,
  GenUiRouteKind.Agent=>GenUiActionExecutionKind.Agent,
  GenUiRouteKind.Capability=>GenUiActionExecutionKind.Capability,
  GenUiRouteKind.External=>GenUiActionExecutionKind.External,
  _=>null
 };
 private static void ValidateFields(IReadOnlyList<GenUiSchemaField> fields,string owner,List<string> errors)
 {
  foreach(var field in fields)
   if(string.IsNullOrWhiteSpace(field.Key)||!Enum.IsDefined(field.Type)) errors.Add($"{owner} fields require a key and supported value type.");
 }
 private static bool Matches(System.Text.Json.JsonElement value,GenUiValueType type)=>type switch
 {
  GenUiValueType.String=>value.ValueKind==System.Text.Json.JsonValueKind.String,
  GenUiValueType.Integer=>value.ValueKind==System.Text.Json.JsonValueKind.Number&&value.TryGetInt64(out _),
  GenUiValueType.Number=>value.ValueKind==System.Text.Json.JsonValueKind.Number,
  GenUiValueType.Boolean=>value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
  GenUiValueType.Array=>value.ValueKind==System.Text.Json.JsonValueKind.Array,
  GenUiValueType.Object=>value.ValueKind==System.Text.Json.JsonValueKind.Object,
  GenUiValueType.DateTime=>value.ValueKind==System.Text.Json.JsonValueKind.String&&DateTimeOffset.TryParse(value.GetString(),out _),
  _=>false
 };
 private static IEnumerable<GenUiComponent> Flatten(GenUiComponent root)
 {
  yield return root; foreach(var child in root.Children) foreach(var item in Flatten(child)) yield return item;
 }
}
