import { describe, it, expect, vi } from 'vitest';
import { renderHook, act } from '../../tests/render';
import { useModal } from './useModal';

describe('useModal', () => {
	it('starts closed', () => {
		const { result } = renderHook(() => useModal(() => <div>modal content</div>));
		expect(result.current.isOpen).toBe(false);
	});

	it('modal is null when closed', () => {
		const { result } = renderHook(() => useModal(() => <div>modal content</div>));
		expect(result.current.modal).toBeNull();
	});

	it('opens when open() is called', () => {
		const { result } = renderHook(() => useModal(() => <div>modal content</div>));
		act(() => {
			result.current.open();
		});
		expect(result.current.isOpen).toBe(true);
	});

	it('renders modal content after opening', () => {
		const { result } = renderHook(() => useModal(() => <div data-testid="modal">content</div>));
		act(() => {
			result.current.open();
		});
		expect(result.current.modal).not.toBeNull();
	});

	it('closes when close() is called', () => {
		const { result } = renderHook(() => useModal(() => <div>modal content</div>));
		act(() => {
			result.current.open();
		});
		act(() => {
			result.current.close();
		});
		expect(result.current.isOpen).toBe(false);
		expect(result.current.modal).toBeNull();
	});

	it('passes onClose callback to the render function', () => {
		const renderFn = vi.fn(() => <div>content</div>);
		const { result } = renderHook(() => useModal(renderFn));
		act(() => {
			result.current.open();
		});
		expect(renderFn).toHaveBeenCalledWith(expect.any(Function));
	});

	it('calling the onClose callback passed to renderFn closes the modal', () => {
		let capturedOnClose: (() => void) | null = null;
		const { result } = renderHook(() =>
			useModal((onClose) => {
				capturedOnClose = onClose;
				return <div>content</div>;
			}),
		);
		act(() => {
			result.current.open();
		});
		expect(result.current.isOpen).toBe(true);
		act(() => {
			capturedOnClose?.();
		});
		expect(result.current.isOpen).toBe(false);
	});
});
