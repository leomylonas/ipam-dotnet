import { InlineNotification } from '@carbon/react';
import {
	FetchClientError,
	isJsonApiError,
	isProblemDetails,
	isValidationProblemDetails,
} from '@leomylonas/json-fetch-client';
import styles from './ErrorBanner.module.scss';

interface ErrorBannerProps {
	/** The error to display, or null/undefined when there is no error. */
	error: unknown;
	/** Optional custom title; defaults to "Error". */
	title?: string;
}

/**
 * Extracts one or more human-readable message strings from a FetchClientError,
 * branching on the kind discriminator.
 *
 * - `problem-details`: uses the `detail` field, then appends any `errors`
 *   string array (used by the backend's IdentityOperationException response).
 * - `validation-problem-details`: flattens the `errors` field-keyed map into
 *   a flat list of validation messages.
 * - `jsonapi-error`: uses the error message string.
 * - `unknown`: falls back to the error message string.
 */
function extractLines(error: FetchClientError): string[] {
	const e: unknown = error;

	if (isValidationProblemDetails(e)) {
		// errors is Record<string, string[]> — flatten all per-field arrays into one list.
		const lines = Object.values(e.responseBody.errors).flat();
		if (lines.length > 0) {
			return lines;
		}
	}

	if (isProblemDetails(error)) {
		return [error.responseBody.detail || error.message || 'An error occurred.'];
	}

	if (isJsonApiError(error)) {
		// Prefer the detail field; fall back to title for each JSON:API error object.
		const lines = error.responseBody.errors
			.map((err) => err.detail ?? err.title)
			.filter((s): s is string => typeof s === 'string');
		return lines.length > 0 ? lines : [error.message];
	}

	// For unknowns
	return [error.message || 'An error occurred.'];
}

/**
 * Displays a Carbon InlineNotification when an error is present. Renders
 * nothing when error is null or undefined.
 *
 * When the error is a FetchClientError the kind discriminator is used to
 * extract structured messages (detail field, Identity errors array, validation
 * field errors). Multiple messages are rendered as a bulleted list in the
 * subtitle. Plain Error instances fall back to their message string.
 */
export function ErrorBanner({ error, title = 'Error' }: ErrorBannerProps) {
	if (error === null || error === undefined) {
		return null;
	}

	const errors =
		error instanceof FetchClientError
			? extractLines(error)
			: [error instanceof Error ? error.message : 'An unexpected error occurred. Please try again.'];

	console.log(styles.vertical);

	return (
		<InlineNotification className={styles.vertical} kind="error" title={title} lowContrast hideCloseButton>
			<ul>
				{errors.map((line, index) => (
					<li key={index}>{line}</li>
				))}
			</ul>
		</InlineNotification>
	);
}
