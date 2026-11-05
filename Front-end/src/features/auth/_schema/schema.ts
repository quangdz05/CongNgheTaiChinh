export const loginConstraints = {
  username: { required: true, maxLength: 80 },
  password: { required: true, maxLength: 200 },
} as const;
