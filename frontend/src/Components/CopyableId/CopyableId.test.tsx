import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '../../../tests/render';
import { CopyableId } from './CopyableId';

// Stub the clipboard API — jsdom doesn't implement it.
beforeEach(() => {
	Object.assign(navigator, {
		clipboard: {
			writeText: vi.fn().mockResolvedValue(undefined),
		},
	});
});

describe('CopyableId', () => {
	it('renders the label text', () => {
		render(<CopyableId label="my-label" fullId="full-id-value" />);
		expect(screen.getByText('my-label')).toBeInTheDocument();
	});

	it('renders a copy button', () => {
		render(<CopyableId label="my-label" fullId="full-id-value" />);
		// Carbon icon-only buttons use aria-labelledby rather than title.
		expect(screen.getByRole('button')).toBeInTheDocument();
	});

	it('calls navigator.clipboard.writeText with the fullId when clicked', () => {
		render(<CopyableId label="my-label" fullId="abc-123-def" />);
		fireEvent.click(screen.getByRole('button'));
		expect(navigator.clipboard.writeText).toHaveBeenCalledWith('abc-123-def');
	});

	it('stops click propagation so parent row-click handlers do not fire', () => {
		const parentClickHandler = vi.fn();
		render(
			<>
				{/* eslint-disable-next-line jsx-a11y/click-events-have-key-events, jsx-a11y/no-static-element-interactions */}
				<div onClick={parentClickHandler}>
					<CopyableId label="my-label" fullId="abc-123-def" />
				</div>
			</>,
		);
		fireEvent.click(screen.getByRole('button'));
		expect(parentClickHandler).not.toHaveBeenCalled();
	});
});
