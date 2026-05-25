import { useMemo, useRef, useState } from 'react';
import {
	OverflowMenu,
	OverflowMenuItem,
	SkeletonText,
	Table,
	TableBody,
	TableCell,
	TableContainer,
	TableHead,
	TableHeader,
	TableRow,
	TableToolbar,
	TableToolbarContent,
	TableToolbarSearch,
} from '@carbon/react';
import styles from './IpamDataTable.module.scss';

/**
 * Definition of a single table column. The `render` function receives the full
 * row object and returns any React node, giving callers full control over cell
 * content without sacrificing type safety.
 *
 * Providing `sortValue` makes the column both sortable (click the header to
 * sort) and searchable (the toolbar search matches against this value). It
 * should return a primitive (string or number) — separate from `render` so
 * neither sort nor search logic has to parse React nodes.
 */
export interface ColumnDef<TRow> {
	/** Unique column identifier. */
	key: string;
	/** Text shown in the column header. */
	header: string;
	/** Cell renderer — receives the full row and returns the cell content. */
	render: (row: TRow) => React.ReactNode;
	/**
	 * When provided, the column header becomes sortable and its value contributes
	 * to toolbar search matching.
	 */
	sortValue?: (row: TRow) => string | number;
}

/**
 * An action shown in the per-row overflow menu.
 */
export interface RowAction<TRow> {
	/** Label shown in the overflow menu item. */
	label: string;
	/** Whether this action is styled as destructive (red text). */
	danger?: boolean;
	/** Handler called when the action is clicked. */
	onClick: (row: TRow) => void;
}

interface IpamDataTableProps<TRow extends { id: string }> {
	/** Column definitions. */
	columns: ColumnDef<TRow>[];
	/** Data rows. Each must have a unique string `id` field. */
	rows: TRow[];
	/** Whether to show a skeleton loading state instead of data. */
	isLoading?: boolean;
	/**
	 * Per-row overflow menu actions. When the array is empty or omitted, no
	 * overflow menu column is rendered.
	 */
	rowActions?: RowAction<TRow>[];
	/** Message shown when the rows array is empty (with no active search). */
	emptyMessage?: string;
	/**
	 * Extra content rendered in the toolbar to the right of the search box.
	 * Use for action buttons and sm-height filter inputs that belong inline with
	 * the table. For tall or multi-row filter UIs, render a standalone div above
	 * the table instead.
	 */
	toolbarContent?: React.ReactNode;
	/**
	 * Set to false to hide the built-in toolbar search input. Useful when the
	 * table has its own filter controls in `toolbarContent` that already cover
	 * the search use-case.
	 */
	searchable?: boolean;
	/**
	 * Called whenever the search term changes. Lets the parent intercept the
	 * search value to drive server-side filtering instead of (or in addition to)
	 * the built-in client-side column filter.
	 */
	onSearchChange?: (term: string) => void;
	/**
	 * When provided, rows become clickable and this handler is called on click.
	 * The row cursor changes to a pointer and a hover highlight is applied.
	 */
	onRowClick?: (row: TRow) => void;
}

/**
 * Generic data table built on Carbon primitives. Abstracts the boilerplate of
 * TableContainer, TableHead, TableBody, and per-row overflow menus into a
 * single component so page components stay lean.
 *
 * Features:
 * - Toolbar search: filters visible rows by matching against each column's
 *   `sortValue`. Columns without `sortValue` are excluded from search.
 * - Sortable columns: click a header with `sortValue` to toggle ASC/DESC sort.
 * - Loading skeleton, empty state, and per-row overflow menu actions.
 */
export function IpamDataTable<TRow extends { id: string }>({
	columns,
	rows,
	isLoading = false,
	rowActions = [],
	emptyMessage = 'No data to display.',
	toolbarContent,
	searchable = true,
	onSearchChange,
	onRowClick,
}: IpamDataTableProps<TRow>) {
	const [searchTerm, setSearchTerm] = useState('');
	const [sortKey, setSortKey] = useState<string | null>(null);
	// searchable=false tables have no search term, so filteredRows === sortedRows.
	const [sortDir, setSortDir] = useState<'ASC' | 'DESC'>('ASC');
	const onSearchChangeDebounce = useRef<ReturnType<typeof setTimeout> | null>(null);

	// Apply sort first, then search filter, so sorting operates on the full dataset.
	const sortedRows = useMemo(() => {
		if (sortKey === null) return rows;
		const col = columns.find((c) => c.key === sortKey);
		if (col?.sortValue === undefined) return rows;
		const sv = col.sortValue;
		return [...rows].sort((a, b) => {
			const va = sv(a);
			const vb = sv(b);
			if (va < vb) return sortDir === 'ASC' ? -1 : 1;
			if (va > vb) return sortDir === 'ASC' ? 1 : -1;
			return 0;
		});
	}, [rows, sortKey, sortDir, columns]);

	const filteredRows = useMemo(() => {
		// When onSearchChange is provided, the parent owns filtering — skip client-side search.
		if (!searchable || onSearchChange !== undefined) return sortedRows;
		const term = searchTerm.trim().toLowerCase();
		if (term === '') return sortedRows;
		return sortedRows.filter((row) =>
			columns.some(
				(col) => col.sortValue !== undefined && String(col.sortValue(row)).toLowerCase().includes(term),
			),
		);
	}, [sortedRows, searchTerm, searchable, onSearchChange, columns]);

	function handleSortClick(key: string) {
		if (sortKey === key) {
			setSortDir((d) => (d === 'ASC' ? 'DESC' : 'ASC'));
		} else {
			setSortKey(key);
			setSortDir('ASC');
		}
	}

	const hasActions = rowActions.length > 0;
	const isSearchActive = searchTerm.trim() !== '';
	const effectiveEmptyMessage =
		isSearchActive && filteredRows.length === 0 && rows.length > 0 ? 'No rows match your search.' : emptyMessage;

	return (
		<TableContainer className={styles.container}>
			<TableToolbar>
				<TableToolbarContent>
					{searchable && (
						<TableToolbarSearch
							value={searchTerm}
							onChange={(e) => {
								const value = (e as React.ChangeEvent<HTMLInputElement>).target.value;
								setSearchTerm(value);
								if (onSearchChange !== undefined) {
									if (onSearchChangeDebounce.current !== null) {
										clearTimeout(onSearchChangeDebounce.current);
									}
									onSearchChangeDebounce.current = setTimeout(() => {
										onSearchChange(value);
										onSearchChangeDebounce.current = null;
									}, 400);
								}
							}}
							onClear={() => {
								setSearchTerm('');
								if (onSearchChangeDebounce.current !== null) {
									clearTimeout(onSearchChangeDebounce.current);
									onSearchChangeDebounce.current = null;
								}
								onSearchChange?.('');
							}}
							placeholder="Search…"
							persistent
						/>
					)}
					{toolbarContent}
				</TableToolbarContent>
			</TableToolbar>
			<Table isSortable>
				<TableHead>
					<TableRow>
						{columns.map((col) => (
							<TableHeader
								key={col.key}
								isSortable={col.sortValue !== undefined}
								isSortHeader={sortKey === col.key}
								sortDirection={sortKey === col.key ? sortDir : 'NONE'}
								onClick={col.sortValue !== undefined ? () => handleSortClick(col.key) : undefined}
							>
								{col.header}
							</TableHeader>
						))}
						{/* Reserve an empty header for the actions column. */}
						{hasActions && <TableHeader />}
					</TableRow>
				</TableHead>
				<TableBody>
					{isLoading ? (
						// Skeleton rows — toolbar stays mounted so focus is preserved.
						Array.from({ length: 5 }).map((_, i) => (
							<TableRow key={i}>
								{columns.map((col) => (
									<TableCell key={col.key}>
										<SkeletonText />
									</TableCell>
								))}
								{hasActions && <TableCell />}
							</TableRow>
						))
					) : filteredRows.length === 0 ? (
						// Empty state — a single row spanning all columns.
						<TableRow>
							<TableCell colSpan={columns.length + (hasActions ? 1 : 0)} className={styles.emptyCell}>
								{effectiveEmptyMessage}
							</TableCell>
						</TableRow>
					) : (
						filteredRows.map((row) => (
							<TableRow
								key={row.id}
								className={onRowClick !== undefined ? styles.clickableRow : undefined}
								onClick={
									onRowClick !== undefined
										? () => {
												onRowClick(row);
											}
										: undefined
								}
							>
								{columns.map((col) => (
									<TableCell key={col.key}>{col.render(row)}</TableCell>
								))}
								{hasActions && (
									// Stop propagation so the overflow menu click doesn't also trigger the row click.
									<TableCell
										className={styles.actionsCell}
										onClick={(e) => {
											e.stopPropagation();
										}}
									>
										<OverflowMenu size="sm" flipped iconDescription="Open menu">
											{rowActions.map((action) => (
												<OverflowMenuItem
													key={action.label}
													itemText={action.label}
													isDelete={action.danger === true}
													onClick={() => {
														action.onClick(row);
													}}
												/>
											))}
										</OverflowMenu>
									</TableCell>
								)}
							</TableRow>
						))
					)}
				</TableBody>
			</Table>
		</TableContainer>
	);
}
