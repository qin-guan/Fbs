import type { CodegenConfig } from '@graphql-codegen/cli';

const config: CodegenConfig = {
    schema: 'http://localhost:5204/graphql',
    documents: ['composables/**/*.ts'],
    generates: {
        './gql/': {
            preset: 'client',
            config: {
                useTypeImports: true,
            },
            plugins: [],
        },
    },
};

export default config;
