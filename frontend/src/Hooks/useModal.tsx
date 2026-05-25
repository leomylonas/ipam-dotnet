import { useState, type ReactNode } from 'react';

export function useModal(renderModal: (onClose: () => void) => ReactNode) {
	const [isOpen, setIsOpen] = useState(false);

	function open() {
		setIsOpen(true);
	}

	function close() {
		setIsOpen(false);
	}

	const modal = isOpen ? renderModal(close) : null;

	return { isOpen, open, close, modal };
}
