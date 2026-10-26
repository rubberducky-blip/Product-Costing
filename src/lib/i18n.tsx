import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'

const en = {
  'app.title': 'Product Costing',
  'nav.product': 'Products',
  'nav.merchandise': 'Merchandise',
  'nav.raw_material': 'Raw materials',
  'nav.suppliers': 'Suppliers',
  'nav.settings': 'Settings',
  'nav.logout': 'Log out',

  'kind.product': 'Product',
  'kind.merchandise': 'Merchandise',
  'kind.raw_material': 'Raw material',

  'unit.pcs': 'pcs',
  'unit.cm': 'cm',
  'unit.cm2': 'cm²',
  'unitLong.pcs': 'Pieces',
  'unitLong.cm': 'Centimetres',
  'unitLong.cm2': 'Square centimetres',

  'login.title': 'Sign in',
  'login.email': 'Email',
  'login.password': 'Password',
  'login.submit': 'Sign in',
  'login.notConfigured': 'Supabase is not configured. Set SUPABASE_URL and SUPABASE_ANON_KEY on the host and redeploy.',

  'search.placeholder': 'Search codes, names, suppliers, notes…',
  'search.none': 'No results',

  'common.new': 'New',
  'common.save': 'Save',
  'common.saving': 'Saving…',
  'common.saved': 'Saved',
  'common.cancel': 'Cancel',
  'common.discard': 'Cancel changes',
  'common.delete': 'Delete',
  'common.back': 'Back',
  'common.edit': 'Edit',
  'common.close': 'Close',
  'common.add': 'Add',
  'common.remove': 'Remove',
  'common.loading': 'Loading…',
  'common.none': '—',
  'common.notes': 'Notes',
  'common.default': 'default',
  'common.useDefault': 'Default ({value})',
  'common.filter': 'Filter…',
  'common.empty': 'Nothing here yet.',
  'common.confirmDelete': 'Delete "{name}"? This cannot be undone.',
  'common.lastChange': 'Last change {when} by {who}',
  'common.unsaved': 'Unsaved changes',
  'common.leaveUnsaved': 'You have unsaved changes. Leave anyway?',

  'item.code': 'Code',
  'item.name': 'Name',
  'item.kind': 'Type',
  'item.usableAsMaterial': 'Also used as raw material',
  'item.supplier': 'Supplier',
  'item.noSupplier': 'No supplier',
  'item.supplierCode': 'Supplier code',
  'item.purchasePrice': 'Purchase price (list)',
  'item.purchaseQty': 'Quantity per purchase',
  'item.purchaseQtyHelp': 'How many units the purchase price buys, e.g. a 50×100 sheet = 5000 cm².',
  'item.unit': 'Default unit',
  'item.pieceLength': 'Length of one piece (cm)',
  'item.pieceArea': 'Area of one piece (cm²)',
  'item.pieceHelp': 'Optional. Lets recipes use this material in a different unit.',
  'item.discount': 'Discount %',
  'item.shipping': 'Shipping %',
  'item.customHelp': 'Leave empty to use the supplier default ({value}).',
  'item.sectionLabor': 'Labor time',
  'item.markup': 'Markup %',
  'item.rounding': 'Round price to',
  'item.sectionGeneral': 'General',
  'item.sectionPurchase': 'Purchasing',
  'item.sectionRecipe': 'Recipe',
  'item.sectionPricing': 'Pricing',
  'item.sectionCost': 'Cost breakdown',
  'item.usedIn': 'Used in products',
  'item.new': 'New {kind}',

  'recipe.material': 'Raw material',
  'recipe.quantity': 'Qty',
  'recipe.unit': 'Unit',
  'recipe.comment': 'Comment',
  'recipe.cost': 'Cost',
  'recipe.add': 'Add raw material',
  'recipe.pick': 'Choose raw material…',
  'recipe.empty': 'No raw materials yet.',
  'recipe.noConversion': 'Cannot convert unit — set piece length/area on the material.',

  'labor.seconds': 'Seconds',
  'labor.add': 'Add labor time',
  'labor.empty': 'No labor time yet.',
  'labor.total': 'Total {time}',

  'folder.new': 'New folder',
  'folder.edit': 'Edit folder',
  'folder.name': 'Folder name',
  'folder.parent': 'Inside folder',
  'folder.field': 'Folder',
  'folder.root': '— Top level (no folder)',
  'folder.notEmpty': 'This folder is not empty. Move or delete what is inside it first.',

  'folder.settings': 'Folder settings',
  'folder.listSettings': 'List settings',
  'folder.materialField': 'Folder in raw materials',

  'sort.title': 'Sort',
  'sort.code': 'By code',
  'sort.name': 'By name',
  'sort.manual': 'Manual',

  'notify.title': 'Price changes',
  'notify.empty': 'No price changes yet.',
  'notify.markAll': 'Mark all as read',
  'notify.up': 'Price increased',
  'notify.down': 'Price decreased',
  'notify.changed': 'The price changed because of another change',
  'notify.hide': 'Hide',
  'notify.hideAll': 'Hide all',
  'notify.adjust': 'Adjust markup',
  'notify.adjustHelp': 'Set the markup to {markup} so the price goes back to {price}',
  'notify.adjusted': 'Markup adjusted to the previous price',
  'notify.expansion': 'Margin expansion ({count})',
  'notify.expansionHelp': 'Raises the markup of every item that now sells for less than before, so its price goes back to the previous one',
  'notify.expansionConfirm': 'Raise the markup of {count} item(s) so their prices go back to the previous ones?',

  'cost.listPrice': 'List price',
  'cost.discount': 'Discount',
  'cost.net': 'Net price',
  'cost.shipping': 'Shipping',
  'cost.purchaseTotal': 'Purchase total',
  'cost.unitCost': 'Cost per {unit}',
  'cost.materials': 'Raw materials',
  'cost.labor': 'Labor',
  'cost.overhead': 'Production overhead',
  'cost.total': 'Total cost',
  'cost.cost': 'Cost',
  'cost.markup': 'Markup',
  'cost.rawPrice': 'Price before rounding',
  'cost.price': 'Final price',
  'cost.profit': 'Profit',
  'cost.margin': 'Margin',
  'cost.warnMissing': 'A raw material in the recipe no longer exists.',
  'cost.warnConversion': 'Some recipe lines cannot be costed because of a unit mismatch.',

  'supplier.name': 'Name',
  'supplier.country': 'Country',
  'supplier.discount': 'Default discount %',
  'supplier.shipping': 'Shipping cost %',
  'supplier.shippingHelp': 'Shipping as a percentage of each product’s cost, e.g. 14.',
  'supplier.email': 'Email',
  'supplier.phone': 'Phone',
  'supplier.items': 'Items from this supplier',
  'supplier.new': 'New supplier',

  'settings.title': 'Settings',
  'settings.costs': 'Production costs',
  'settings.laborRate': 'Labor cost per hour',
  'settings.laborRateHelp': 'Wages and employer contributions per working hour.',
  'settings.overheadRate': 'Production overhead per hour',
  'settings.overheadRateHelp': 'General production expenses (rent, power, equipment…) per working hour.',
  'settings.pricing': 'Pricing',
  'settings.defaultMarkup': 'Default markup %',
  'settings.roundingStep': 'Default rounding step',
  'settings.roundingMode': 'Rounding',
  'settings.round.nearest': 'Nearest',
  'settings.round.up': 'Up',
  'settings.round.down': 'Down',
  'settings.currency': 'Currency symbol',
  'settings.language': 'Language',
  'settings.backups': 'Backups',
  'settings.backupsHelp': 'A backup is taken automatically every day at 02:00 UTC and kept for the retention period. Manual backups are kept until deleted. Download backups regularly to keep an offline copy.',
  'settings.retention': 'Keep automatic backups (days)',
  'settings.backupNow': 'Back up now',
  'settings.restoreFile': 'Restore from file…',
  'settings.download': 'Download',
  'settings.restore': 'Restore',
  'settings.confirmRestore': 'Restore this backup? All current data will be replaced (a safety backup is taken first).',
  'settings.restored': 'Backup restored',
  'settings.backupCreated': 'Backup created',
  'settings.noBackups': 'No backups yet.',
  'backup.auto': 'Automatic',
  'backup.manual': 'Manual',
  'backup.pre_restore': 'Before restore',

  'list.cost': 'Cost',
  'list.price': 'Price',
  'list.margin': 'Margin',
  'list.supplier': 'Supplier',
  'list.unitCost': 'Unit cost',
  'list.country': 'Country',
  'list.discount': 'Discount',
  'list.shipping': 'Shipping',
  'list.items': 'Items',

  'presence.editing': '{who} is also viewing this record. Coordinate before saving.',
  'presence.changed': '{who} saved a newer version of this record.',
  'presence.reload': 'Load latest',
  'conflict.title': 'Someone else saved first',
  'conflict.body': '{who} changed this record while you were editing. Load their version (your changes are lost) or overwrite it with yours.',
  'conflict.reload': 'Load their version',
  'conflict.overwrite': 'Overwrite with mine',
  'conflict.deleted': 'This record was deleted by another user.',

  'err.generic': 'Something went wrong. Please try again.',
  'err.conflict': 'Another user changed this record. Reload and try again.',
  'err.inUse': 'This item is used elsewhere (e.g. in a recipe) and cannot be removed or changed this way.',
  'err.invalidMaterial': 'Only raw materials (or merchandise marked as raw material) can be used in a recipe.',
  'err.invalidBackup': 'This file is not a valid backup.',
  'err.notFound': 'Record not found.',
  'err.duplicate': 'This code or name already exists.',
  'err.login': 'Wrong email or password.',
  'err.emailNotConfirmed': 'This user is not confirmed yet. In Supabase → Authentication → Users, confirm the user (or recreate it with "Auto confirm").',
  'err.network': 'Cannot reach Supabase. Check SUPABASE_URL and your internet connection.',
  'err.required': 'Please fill in the required fields.',
}

export type TKey = keyof typeof en

const el: Record<TKey, string> = {
  'app.title': 'Κοστολόγηση',
  'nav.product': 'Προϊόντα',
  'nav.merchandise': 'Εμπορεύματα',
  'nav.raw_material': 'Πρώτες ύλες',
  'nav.suppliers': 'Προμηθευτές',
  'nav.settings': 'Ρυθμίσεις',
  'nav.logout': 'Αποσύνδεση',

  'kind.product': 'Προϊόν',
  'kind.merchandise': 'Εμπόρευμα',
  'kind.raw_material': 'Πρώτη ύλη',

  'unit.pcs': 'τεμ.',
  'unit.cm': 'εκ.',
  'unit.cm2': 'εκ²',
  'unitLong.pcs': 'Τεμάχια',
  'unitLong.cm': 'Εκατοστά',
  'unitLong.cm2': 'Τετραγωνικά εκατοστά',

  'login.title': 'Σύνδεση',
  'login.email': 'Email',
  'login.password': 'Κωδικός',
  'login.submit': 'Σύνδεση',
  'login.notConfigured': 'Το Supabase δεν έχει ρυθμιστεί. Ορίστε SUPABASE_URL και SUPABASE_ANON_KEY στη φιλοξενία και κάντε νέο deploy.',

  'search.placeholder': 'Αναζήτηση κωδικών, ονομάτων, προμηθευτών, σημειώσεων…',
  'search.none': 'Κανένα αποτέλεσμα',

  'common.new': 'Νέο',
  'common.save': 'Αποθήκευση',
  'common.saving': 'Αποθήκευση…',
  'common.saved': 'Αποθηκεύτηκε',
  'common.cancel': 'Ακύρωση',
  'common.discard': 'Ακύρωση αλλαγών',
  'common.delete': 'Διαγραφή',
  'common.back': 'Πίσω',
  'common.edit': 'Επεξεργασία',
  'common.close': 'Κλείσιμο',
  'common.add': 'Προσθήκη',
  'common.remove': 'Αφαίρεση',
  'common.loading': 'Φόρτωση…',
  'common.none': '—',
  'common.notes': 'Σημειώσεις',
  'common.default': 'προεπιλογή',
  'common.useDefault': 'Προεπιλογή ({value})',
  'common.filter': 'Φίλτρο…',
  'common.empty': 'Δεν υπάρχει τίποτα ακόμα.',
  'common.confirmDelete': 'Διαγραφή «{name}»; Η ενέργεια δεν αναιρείται.',
  'common.lastChange': 'Τελευταία αλλαγή {when} από {who}',
  'common.unsaved': 'Μη αποθηκευμένες αλλαγές',
  'common.leaveUnsaved': 'Υπάρχουν μη αποθηκευμένες αλλαγές. Έξοδος;',

  'item.code': 'Κωδικός',
  'item.name': 'Όνομα',
  'item.kind': 'Τύπος',
  'item.usableAsMaterial': 'Χρησιμοποιείται και ως πρώτη ύλη',
  'item.supplier': 'Προμηθευτής',
  'item.noSupplier': 'Χωρίς προμηθευτή',
  'item.supplierCode': 'Κωδικός προμηθευτή',
  'item.purchasePrice': 'Τιμή αγοράς (καταλόγου)',
  'item.purchaseQty': 'Ποσότητα ανά αγορά',
  'item.purchaseQtyHelp': 'Πόσες μονάδες αντιστοιχούν στην τιμή αγοράς, π.χ. φύλλο 50×100 = 5000 εκ².',
  'item.unit': 'Προεπιλεγμένη μονάδα',
  'item.pieceLength': 'Μήκος ενός τεμαχίου (εκ.)',
  'item.pieceArea': 'Εμβαδόν ενός τεμαχίου (εκ²)',
  'item.pieceHelp': 'Προαιρετικό. Επιτρέπει χρήση σε συνταγές με άλλη μονάδα.',
  'item.discount': 'Έκπτωση %',
  'item.shipping': 'Μεταφορικά %',
  'item.customHelp': 'Κενό = προεπιλογή προμηθευτή ({value}).',
  'item.sectionLabor': 'Χρόνοι εργασίας',
  'item.markup': 'Markup %',
  'item.rounding': 'Στρογγυλοποίηση τιμής στο',
  'item.sectionGeneral': 'Γενικά',
  'item.sectionPurchase': 'Αγορά',
  'item.sectionRecipe': 'Συνταγή',
  'item.sectionPricing': 'Τιμολόγηση',
  'item.sectionCost': 'Ανάλυση κόστους',
  'item.usedIn': 'Χρησιμοποιείται στα προϊόντα',
  'item.new': 'Νέο: {kind}',

  'recipe.material': 'Πρώτη ύλη',
  'recipe.quantity': 'Ποσότητα',
  'recipe.unit': 'Μονάδα',
  'recipe.comment': 'Σχόλιο',
  'recipe.cost': 'Κόστος',
  'recipe.add': 'Προσθήκη πρώτης ύλης',
  'recipe.pick': 'Επιλογή πρώτης ύλης…',
  'recipe.empty': 'Δεν υπάρχουν πρώτες ύλες ακόμα.',
  'recipe.noConversion': 'Δεν γίνεται μετατροπή μονάδας — ορίστε μήκος/εμβαδόν τεμαχίου στην πρώτη ύλη.',

  'labor.seconds': 'Δευτερόλεπτα',
  'labor.add': 'Προσθήκη χρόνου εργασίας',
  'labor.empty': 'Δεν υπάρχει χρόνος εργασίας.',
  'labor.total': 'Σύνολο {time}',

  'folder.new': 'Νέος φάκελος',
  'folder.edit': 'Επεξεργασία φακέλου',
  'folder.name': 'Όνομα φακέλου',
  'folder.parent': 'Μέσα στον φάκελο',
  'folder.field': 'Φάκελος',
  'folder.root': '— Αρχικό επίπεδο (χωρίς φάκελο)',
  'folder.notEmpty': 'Ο φάκελος δεν είναι άδειος. Μετακινήστε ή διαγράψτε πρώτα ό,τι περιέχει.',

  'folder.settings': 'Ρυθμίσεις φακέλου',
  'folder.listSettings': 'Ρυθμίσεις λίστας',
  'folder.materialField': 'Φάκελος στις πρώτες ύλες',

  'sort.title': 'Ταξινόμηση',
  'sort.code': 'Κατά κωδικό',
  'sort.name': 'Κατά όνομα',
  'sort.manual': 'Χειροκίνητη',

  'notify.title': 'Αλλαγές τιμών',
  'notify.empty': 'Δεν υπάρχουν αλλαγές τιμών.',
  'notify.markAll': 'Όλες ως αναγνωσμένες',
  'notify.up': 'Αύξηση τιμής',
  'notify.down': 'Μείωση τιμής',
  'notify.changed': 'Η τιμή άλλαξε λόγω άλλης αλλαγής',
  'notify.hide': 'Απόκρυψη',
  'notify.hideAll': 'Απόκρυψη όλων',
  'notify.adjust': 'Προσαρμογή markup',
  'notify.adjustHelp': 'Το markup γίνεται {markup} ώστε η τιμή να επιστρέψει στα {price}',
  'notify.adjusted': 'Το markup προσαρμόστηκε στην προηγούμενη τιμή',
  'notify.expansion': 'Margin expansion ({count})',
  'notify.expansionHelp': 'Αυξάνει το markup σε όσα είδη πωλούνται πλέον φθηνότερα, ώστε η τιμή τους να επιστρέψει στην προηγούμενη',
  'notify.expansionConfirm': 'Να αυξηθεί το markup σε {count} είδη ώστε οι τιμές τους να επιστρέψουν στις προηγούμενες;',

  'cost.listPrice': 'Τιμή καταλόγου',
  'cost.discount': 'Έκπτωση',
  'cost.net': 'Καθαρή τιμή',
  'cost.shipping': 'Μεταφορικά',
  'cost.purchaseTotal': 'Σύνολο αγοράς',
  'cost.unitCost': 'Κόστος ανά {unit}',
  'cost.materials': 'Πρώτες ύλες',
  'cost.labor': 'Εργασία',
  'cost.overhead': 'Γενικά έξοδα παραγωγής',
  'cost.total': 'Συνολικό κόστος',
  'cost.cost': 'Κόστος',
  'cost.markup': 'Markup',
  'cost.rawPrice': 'Τιμή πριν τη στρογγυλοποίηση',
  'cost.price': 'Τελική τιμή',
  'cost.profit': 'Κέρδος',
  'cost.margin': 'Περιθώριο (margin)',
  'cost.warnMissing': 'Μια πρώτη ύλη της συνταγής δεν υπάρχει πλέον.',
  'cost.warnConversion': 'Κάποιες γραμμές δεν κοστολογούνται λόγω ασυμβατότητας μονάδων.',

  'supplier.name': 'Επωνυμία',
  'supplier.country': 'Χώρα',
  'supplier.discount': 'Προεπιλεγμένη έκπτωση %',
  'supplier.shipping': 'Κόστος μεταφορικών %',
  'supplier.shippingHelp': 'Μεταφορικά ως ποσοστό του κόστους κάθε προϊόντος, π.χ. 14.',
  'supplier.email': 'Email',
  'supplier.phone': 'Τηλέφωνο',
  'supplier.items': 'Είδη από αυτόν τον προμηθευτή',
  'supplier.new': 'Νέος προμηθευτής',

  'settings.title': 'Ρυθμίσεις',
  'settings.costs': 'Κόστη παραγωγής',
  'settings.laborRate': 'Κόστος εργασίας ανά ώρα',
  'settings.laborRateHelp': 'Μισθοί και εργοδοτικές εισφορές ανά ώρα εργασίας.',
  'settings.overheadRate': 'Γενικά έξοδα παραγωγής ανά ώρα',
  'settings.overheadRateHelp': 'Γενικά έξοδα παραγωγής (ενοίκιο, ρεύμα, εξοπλισμός…) ανά ώρα εργασίας.',
  'settings.pricing': 'Τιμολόγηση',
  'settings.defaultMarkup': 'Προεπιλεγμένο markup %',
  'settings.roundingStep': 'Προεπιλεγμένο βήμα στρογγυλοποίησης',
  'settings.roundingMode': 'Στρογγυλοποίηση',
  'settings.round.nearest': 'Πλησιέστερη',
  'settings.round.up': 'Προς τα πάνω',
  'settings.round.down': 'Προς τα κάτω',
  'settings.currency': 'Σύμβολο νομίσματος',
  'settings.language': 'Γλώσσα',
  'settings.backups': 'Αντίγραφα ασφαλείας',
  'settings.backupsHelp': 'Λαμβάνεται αυτόματα αντίγραφο κάθε μέρα στις 02:00 UTC και διατηρείται για την περίοδο διατήρησης. Τα χειροκίνητα αντίγραφα διατηρούνται μέχρι να διαγραφούν. Κατεβάζετε τακτικά αντίγραφα για offline φύλαξη.',
  'settings.retention': 'Διατήρηση αυτόματων αντιγράφων (ημέρες)',
  'settings.backupNow': 'Αντίγραφο τώρα',
  'settings.restoreFile': 'Επαναφορά από αρχείο…',
  'settings.download': 'Λήψη',
  'settings.restore': 'Επαναφορά',
  'settings.confirmRestore': 'Επαναφορά αυτού του αντιγράφου; Όλα τα τρέχοντα δεδομένα θα αντικατασταθούν (λαμβάνεται πρώτα αντίγραφο ασφαλείας).',
  'settings.restored': 'Η επαναφορά ολοκληρώθηκε',
  'settings.backupCreated': 'Δημιουργήθηκε αντίγραφο',
  'settings.noBackups': 'Δεν υπάρχουν αντίγραφα ακόμα.',
  'backup.auto': 'Αυτόματο',
  'backup.manual': 'Χειροκίνητο',
  'backup.pre_restore': 'Πριν την επαναφορά',

  'list.cost': 'Κόστος',
  'list.price': 'Τιμή',
  'list.margin': 'Margin',
  'list.supplier': 'Προμηθευτής',
  'list.unitCost': 'Κόστος μονάδας',
  'list.country': 'Χώρα',
  'list.discount': 'Έκπτωση',
  'list.shipping': 'Μεταφορικά',
  'list.items': 'Είδη',

  'presence.editing': 'Ο/Η {who} βλέπει επίσης αυτή την εγγραφή. Συνεννοηθείτε πριν αποθηκεύσετε.',
  'presence.changed': 'Ο/Η {who} αποθήκευσε νεότερη έκδοση αυτής της εγγραφής.',
  'presence.reload': 'Φόρτωση νεότερης',
  'conflict.title': 'Κάποιος άλλος αποθήκευσε πρώτος',
  'conflict.body': 'Ο/Η {who} άλλαξε αυτή την εγγραφή ενώ την επεξεργαζόσασταν. Φορτώστε τη δική του/της έκδοση (οι αλλαγές σας χάνονται) ή αντικαταστήστε την με τη δική σας.',
  'conflict.reload': 'Φόρτωση της άλλης έκδοσης',
  'conflict.overwrite': 'Αντικατάσταση με τη δική μου',
  'conflict.deleted': 'Η εγγραφή διαγράφηκε από άλλο χρήστη.',

  'err.generic': 'Κάτι πήγε στραβά. Δοκιμάστε ξανά.',
  'err.conflict': 'Άλλος χρήστης άλλαξε την εγγραφή. Φορτώστε ξανά και δοκιμάστε.',
  'err.inUse': 'Το είδος χρησιμοποιείται αλλού (π.χ. σε συνταγή) και δεν μπορεί να αφαιρεθεί ή να αλλάξει έτσι.',
  'err.invalidMaterial': 'Σε συνταγή μπαίνουν μόνο πρώτες ύλες (ή εμπορεύματα που χρησιμοποιούνται ως πρώτη ύλη).',
  'err.invalidBackup': 'Το αρχείο δεν είναι έγκυρο αντίγραφο ασφαλείας.',
  'err.notFound': 'Η εγγραφή δεν βρέθηκε.',
  'err.duplicate': 'Ο κωδικός ή το όνομα υπάρχει ήδη.',
  'err.login': 'Λάθος email ή κωδικός.',
  'err.emailNotConfirmed': 'Ο χρήστης δεν έχει επιβεβαιωθεί. Στο Supabase → Authentication → Users επιβεβαιώστε τον (ή δημιουργήστε τον ξανά με «Auto confirm»).',
  'err.network': 'Δεν υπάρχει σύνδεση με το Supabase. Ελέγξτε το SUPABASE_URL και τη σύνδεση στο internet.',
  'err.required': 'Συμπληρώστε τα υποχρεωτικά πεδία.',
}

export type Lang = 'el' | 'en'
const dicts: Record<Lang, Record<TKey, string>> = { en, el }

interface I18n {
  lang: Lang
  setLang: (l: Lang) => void
  t: (key: TKey | string, vars?: Record<string, string | number>) => string
}

const Ctx = createContext<I18n | null>(null)

function initialLang(): Lang {
  try {
    const saved = localStorage.getItem('lang')
    if (saved === 'el' || saved === 'en') return saved
  } catch {
    /* storage unavailable */
  }
  return navigator.language?.startsWith('el') ? 'el' : 'en'
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const [lang, setLangState] = useState<Lang>(initialLang)

  useEffect(() => {
    document.documentElement.lang = lang
    document.title = dicts[lang]['app.title']
  }, [lang])

  const setLang = (l: Lang) => {
    setLangState(l)
    try {
      localStorage.setItem('lang', l)
    } catch {
      /* storage unavailable */
    }
  }

  const t: I18n['t'] = (key, vars) => {
    let s = dicts[lang][key as TKey] ?? dicts.en[key as TKey] ?? key
    if (vars) for (const [k, v] of Object.entries(vars)) s = s.replaceAll(`{${k}}`, String(v))
    return s
  }

  return <Ctx.Provider value={{ lang, setLang, t }}>{children}</Ctx.Provider>
}

export function useI18n() {
  const c = useContext(Ctx)
  if (!c) throw new Error('useI18n outside provider')
  return c
}
