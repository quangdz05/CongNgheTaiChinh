export const transactionConstraints = {
  amount: { min: 1, max: 100000000000 },
  currency: "VND",
  countryPattern: "[A-Z]{2}",
  notes: { minLength: 5, maxLength: 1000 },
} as const;
