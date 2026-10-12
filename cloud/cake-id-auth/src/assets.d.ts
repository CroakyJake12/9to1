declare module "*.txt" {
  const contents: string;
  export default contents;
}

declare module "*.sql" { const text: string; export default text; }
