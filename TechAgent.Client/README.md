# TechAgent.Client

Angular 19 frontend for TechAgent — Syncfusion-powered chat UI.

## Environment Setup

`environment.ts` and `environment.prod.ts` are gitignored because they contain the Syncfusion license key.
Copy the example file and fill in your real values before running or building:

```bash
cp src/environments/environment.example.ts src/environments/environment.ts
# Edit environment.ts and replace YOUR_SYNCFUSION_KEY with your real key
```

Get your free community license key at [syncfusion.com/products/communitylicense](https://www.syncfusion.com/products/communitylicense).

The Syncfusion key is registered once at startup in `main.ts` via `environment.syncfusionLicenseKey` — it is never hardcoded in source.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Karma](https://karma-runner.github.io) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
