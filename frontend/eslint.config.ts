/// <reference types="node" />
import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import pluginReact from 'eslint-plugin-react';
import pluginReactHooks from 'eslint-plugin-react-hooks';
import pluginJsxA11y from 'eslint-plugin-jsx-a11y';
import pluginImport from 'eslint-plugin-import';
import pluginPrettier from 'eslint-plugin-prettier/recommended';
import globals from 'globals';

export default tseslint.config(
	// Exclude build output and dependency directories.
	{ ignores: ['dist/**', 'node_modules/**'] },

	// JavaScript baseline.
	js.configs.recommended,

	// TypeScript strict + stylistic presets — covers parser setup automatically.
	...tseslint.configs.strict,
	...tseslint.configs.stylistic,

	// Prettier — disables conflicting formatting rules and reports Prettier
	// violations as ESLint errors. Must come after other style configs.
	pluginPrettier,

	{
		// Restrict type-aware linting to app source only. Config files
		// (eslint.config.ts, vite.config.ts) are excluded because tsconfig.json
		// deliberately does not include them — they have their own tsconfig.config.json.
		files: ['src/**/*.{ts,tsx}'],

		plugins: {
			react: pluginReact,
			'react-hooks': pluginReactHooks,
			'jsx-a11y': pluginJsxA11y,
			import: pluginImport,
		},

		languageOptions: {
			globals: { ...globals.browser },
			parserOptions: {
				ecmaFeatures: { jsx: true },
				// Type-aware linting — resolves types from tsconfig.json.
				project: true,
				tsconfigRootDir: import.meta.dirname,
			},
		},

		settings: {
			// Tell eslint-plugin-react which version to target.
			react: { version: 'detect' },
			// Tell eslint-plugin-import how to resolve TypeScript paths.
			'import/resolver': { typescript: true },
		},

		rules: {
			// ── React ──────────────────────────────────────────────────────
			...pluginReact.configs.recommended.rules,
			// React 17+ JSX transform — no need to import React in every file.
			'react/react-in-jsx-scope': 'off',
			'react/prop-types': 'off',

			// ── React Hooks ────────────────────────────────────────────────
			...pluginReactHooks.configs.recommended.rules,
			'react-hooks/exhaustive-deps': 'error',

			// ── Accessibility ──────────────────────────────────────────────
			...pluginJsxA11y.configs.recommended.rules,
			'jsx-a11y/no-autofocus': 'off',

			// ── TypeScript (from the plan's rule posture) ──────────────────
			'@typescript-eslint/no-explicit-any': 'error',
			'@typescript-eslint/no-floating-promises': 'error',
			'@typescript-eslint/no-unused-vars': 'error',
			'@typescript-eslint/no-non-null-assertion': 'warn',
			'@typescript-eslint/consistent-type-imports': [
				'error',
				{ prefer: 'type-imports', fixStyle: 'inline-type-imports' },
			],
		},
	},
);
